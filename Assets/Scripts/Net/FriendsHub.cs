using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Friends;
using Unity.Services.Friends.Models;
using Unity.Services.Friends.Notifications;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// The friends list: who you know, who is online, and which of them you can walk straight into a
    /// match with.
    ///
    /// Friends are added by the public <c>Name#1234</c> rather than by the raw PlayerId. Both work —
    /// the service offers each — but a PlayerId is a 28-character opaque string nobody can read out
    /// over a phone or copy off a friend's screen, so it is not offered anywhere in the UI.
    ///
    /// Adding is a request the other player accepts, not something either can do unilaterally. That
    /// is the service's own model and it is the right one here: presence tells a friend when you are
    /// online, which is not something a stranger who guessed a name should be able to switch on.
    ///
    /// Like <see cref="OnlineSession"/> and <see cref="PlayerAccount"/>, nothing throws at the caller.
    /// </summary>
    public static class FriendsHub
    {
        /// <summary>
        /// What this player is broadcasting to friends. One field, because the only question a friend
        /// asks is "can I join you", and the join code is the whole answer.
        ///
        /// Must be a plain serializable type with a parameterless constructor: the service serialises
        /// it into the presence record and rehydrates it on the other side.
        /// </summary>
        [Serializable]
        public class TableActivity
        {
            /// <summary>The host's Relay code, or empty when not hosting a joinable table.</summary>
            public string JoinCode = string.Empty;
        }

        /// <summary>
        /// The message a friend sends over <see cref="FriendsService.MessageAsync{T}"/> to invite this
        /// player straight into their table, rather than the player finding it themselves from a Join
        /// button on the friend's row.
        /// </summary>
        [Serializable]
        public class InviteMessage
        {
            /// <summary>Either <see cref="KindInvite"/> or <see cref="KindDecline"/>.</summary>
            public string Kind = KindInvite;

            public string FromName = string.Empty;
            public string JoinCode = string.Empty;
        }

        /// <summary>Come and play — carries the table's code.</summary>
        public const string KindInvite = "invite";

        /// <summary>No thanks. Carries no code: there is nothing for the sender to join.</summary>
        public const string KindDecline = "decline";

        /// <summary>An invite waiting to be shown, or acted on. One at a time — see <see cref="OnMessageReceived"/>.</summary>
        public readonly struct Invite
        {
            public readonly string FromMemberId;
            public readonly string FromName;
            public readonly string JoinCode;

            public Invite(string fromMemberId, string fromName, string joinCode)
            {
                FromMemberId = fromMemberId;
                FromName = fromName;
                JoinCode = joinCode;
            }
        }

        private static bool initialised;

        public static string LastError { get; private set; } = string.Empty;

        public static bool IsBusy { get; private set; }

        public static bool IsReady => initialised;

        /// <summary>Raised on any change to the lists or to anyone's presence, for the UI to redraw.</summary>
        public static event Action OnChanged;

        /// <summary>
        /// A friend has turned down the invitation this player sent. Carries their name.
        ///
        /// Worth a message of its own rather than a silence: the inviter is sitting at a table they
        /// opened specifically for this person, and nothing else would ever tell them that nobody is
        /// coming. They would wait until they gave up.
        /// </summary>
        public static event Action<string> OnInviteDeclined;

        /// <summary>
        /// The most recent invite nobody has answered yet, or null.
        ///
        /// Deliberately a single slot rather than a queue. This is a call to come play, not a chat
        /// thread — if two arrive, the second is the one that is still true, and holding on to the
        /// first after that would only invite the player to a table that has likely moved on.
        /// </summary>
        public static Invite? PendingInvite { get; private set; }

        public static IReadOnlyList<Relationship> Friends =>
            initialised ? FriendsService.Instance.Friends : Array.Empty<Relationship>();

        public static IReadOnlyList<Relationship> IncomingRequests =>
            initialised ? FriendsService.Instance.IncomingFriendRequests : Array.Empty<Relationship>();

        public static IReadOnlyList<Relationship> OutgoingRequests =>
            initialised ? FriendsService.Instance.OutgoingFriendRequests : Array.Empty<Relationship>();

        /// <summary>
        /// Brings the service up. Safe to call repeatedly; only the first does anything.
        ///
        /// Sign-in first, always — Friends refuses every call without a token, exactly as Relay does.
        /// </summary>
        public static async Task<bool> EnsureReadyAsync()
        {
            if (initialised)
            {
                return true;
            }

            if (!await GameServices.EnsureSignedInAsync())
            {
                return Fail("Could not reach the server", null);
            }

            try
            {
                await FriendsService.Instance.InitializeAsync();

                FriendsService.Instance.RelationshipAdded += OnRelationshipAdded;
                FriendsService.Instance.RelationshipDeleted += OnRelationshipDeleted;
                FriendsService.Instance.PresenceUpdated += OnPresenceUpdated;
                FriendsService.Instance.MessageReceived += OnMessageReceived;

                initialised = true;

                // Announce availability immediately. A player sitting in the menu is online and
                // joinable-in-principle, and a friends list where everyone reads as offline until they
                // happen to host is not worth opening.
                await SetPresenceAsync(string.Empty);

                OnChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                return Fail("Could not load your friends", e);
            }
        }

        /// <summary>
        /// Forgets the service, so the next <see cref="EnsureReadyAsync"/> starts it again for
        /// whoever is signed in by then.
        ///
        /// Needed because the friends list belongs to a player, not to the app. After the player is
        /// deleted and replaced by a fresh anonymous one, leaving this initialised would show the new
        /// player the old one's friends and publish presence under an identity that no longer exists.
        /// </summary>
        public static void Reset()
        {
            if (initialised)
            {
                FriendsService.Instance.RelationshipAdded -= OnRelationshipAdded;
                FriendsService.Instance.RelationshipDeleted -= OnRelationshipDeleted;
                FriendsService.Instance.PresenceUpdated -= OnPresenceUpdated;
                FriendsService.Instance.MessageReceived -= OnMessageReceived;
                initialised = false;
            }

            // An invite sent to the player who is being replaced is not one the new player should
            // wake up to.
            PendingInvite = null;

            OnChanged?.Invoke();
        }

        // ---------- the list ----------

        /// <summary>
        /// Sends a friend request to a <c>Name#1234</c>. They see it and choose.
        /// </summary>
        public static async Task<bool> SendRequestAsync(string nameWithTag)
        {
            if (string.IsNullOrWhiteSpace(nameWithTag))
            {
                return Fail("Enter a friend code", null);
            }

            string name = nameWithTag.Trim();

            // The tag is not decoration — several players may share a name and it is the only thing
            // telling them apart. Caught here so the mistake is named, rather than coming back as a
            // player-not-found for a name that plainly exists.
            if (!name.Contains('#'))
            {
                return Fail("Include the #number, e.g. Player#1234", null);
            }

            if (string.Equals(name, PlayerAccount.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return Fail("That is your own code", null);
            }

            if (!await EnsureReadyAsync())
            {
                return false;
            }

            IsBusy = true;
            try
            {
                await FriendsService.Instance.AddFriendByNameAsync(name);
                OnChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                return Fail("No player with that code", e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Accepts an incoming request. Adding back is how the service records agreement — the
        /// pending request becomes a friendship rather than there being a separate accept call.
        /// </summary>
        public static async Task<bool> AcceptAsync(string memberId) =>
            await RunAsync(() => FriendsService.Instance.AddFriendAsync(memberId),
                           "Could not accept that request");

        public static async Task<bool> DeclineAsync(string memberId) =>
            await RunAsync(() => FriendsService.Instance.DeleteIncomingFriendRequestAsync(memberId),
                           "Could not decline that request");

        public static async Task<bool> RemoveAsync(string memberId) =>
            await RunAsync(() => FriendsService.Instance.DeleteFriendAsync(memberId),
                           "Could not remove that friend");

        // ---------- inviting ----------

        /// <summary>
        /// Invites a friend straight into a table, rather than making them find one themselves.
        ///
        /// If this player is not already hosting, a table is opened first — inviting someone is asking
        /// them to a game that has to exist. If they already are, the friend is invited to that same
        /// table, so inviting a second friend to a table already open for one does not open a second
        /// one underneath it.
        /// </summary>
        public static async Task<bool> SendInviteAsync(string targetMemberId)
        {
            if (string.IsNullOrEmpty(targetMemberId))
            {
                return Fail("No player to invite", null);
            }

            // Already a guest at somebody else's table: the code is theirs, the table is full, and
            // sending it would invite a friend to a seat that does not exist.
            if (OnlineSession.IsInSession && !OnlineSession.IsHost)
            {
                return Fail("Leave this table before inviting", null);
            }

            if (!OnlineSession.IsInSession && !await OnlineSession.HostAsync())
            {
                return Fail(OnlineSession.LastError, null);
            }

            IsBusy = true;
            try
            {
                var invite = new InviteMessage
                {
                    Kind = KindInvite,
                    FromName = PlayerAccount.DisplayName,
                    JoinCode = OnlineSession.JoinCode
                };

                await FriendsService.Instance.MessageAsync(targetMemberId, invite);
                return true;
            }
            catch (Exception e)
            {
                return Fail("Could not send the invite", e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Turns the invitation down and tells whoever sent it.
        ///
        /// Cleared locally whether or not the reply gets through. Refusing is this player's decision
        /// and it has been made; a service that will not carry the word is the sender's problem to
        /// wait out, not a reason to keep asking this player again.
        /// </summary>
        public static async Task DeclineInviteAsync()
        {
            Invite? invite = PendingInvite;

            PendingInvite = null;
            OnChanged?.Invoke();

            if (!invite.HasValue || !initialised)
            {
                return;
            }

            try
            {
                await FriendsService.Instance.MessageAsync(invite.Value.FromMemberId, new InviteMessage
                {
                    Kind = KindDecline,
                    FromName = PlayerAccount.DisplayName
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not tell {invite.Value.FromName} the invite was declined: {e.Message}");
            }
        }

        /// <summary>Clears the pending invite without acting on it, and without telling the sender.</summary>
        public static void DismissInvite()
        {
            PendingInvite = null;
            OnChanged?.Invoke();
        }

        // ---------- presence ----------

        /// <summary>
        /// Publishes what this player is doing. An empty code means online but not joinable, which is
        /// the normal state everywhere outside a hosted table.
        ///
        /// Called from <see cref="OnlineSession"/> as sessions come and go, so the friends list
        /// follows the player's real state without anything having to remember to update it.
        /// </summary>
        public static async Task SetPresenceAsync(string joinCode)
        {
            if (!initialised)
            {
                return;
            }

            try
            {
                await FriendsService.Instance.SetPresenceAsync(
                    Availability.Online,
                    new TableActivity { JoinCode = joinCode ?? string.Empty });
            }
            catch (Exception e)
            {
                // Presence is a courtesy to other people's screens. A player whose own match is going
                // fine should never see an error because the service would not take an update.
                Debug.LogWarning($"Could not publish presence: {e.Message}");
            }
        }

        /// <summary>
        /// The code needed to join this friend, or empty if they are not hosting a joinable table.
        /// Callers use emptiness to decide whether a Join button does anything.
        /// </summary>
        public static string JoinCodeFor(Relationship relationship)
        {
            Presence presence = relationship?.Member?.Presence;
            if (presence == null || presence.Availability != Availability.Online)
            {
                return string.Empty;
            }

            try
            {
                return presence.GetActivity<TableActivity>()?.JoinCode ?? string.Empty;
            }
            catch (Exception)
            {
                // A friend on an older build, or one whose activity is some other shape entirely.
                // Not being able to read it means not joinable, which is already the right answer.
                return string.Empty;
            }
        }

        /// <summary>
        /// Whether a player id belongs to somebody on the friends list.
        ///
        /// Answered from the list already in memory rather than by asking the service: it is called
        /// as a match ends, where a round trip would arrive after the result screen it is for. An
        /// unloaded list answers false, which costs the player one quest tick and never miscounts a
        /// stranger as a friend.
        /// </summary>
        public static bool IsFriend(string memberId)
        {
            if (string.IsNullOrEmpty(memberId))
            {
                return false;
            }

            foreach (Relationship friend in Friends)
            {
                if (friend?.Member?.Id == memberId)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsOnline(Relationship relationship) =>
            relationship?.Member?.Presence != null
            && relationship.Member.Presence.Availability == Availability.Online;

        /// <summary>The friend's public name, or a placeholder if the service has not sent it yet.</summary>
        public static string NameOf(Relationship relationship)
        {
            string name = relationship?.Member?.Profile?.Name;
            return string.IsNullOrEmpty(name) ? "Player" : name;
        }

        // ---------- plumbing ----------

        private static async Task<bool> RunAsync(Func<Task> call, string failureMessage)
        {
            if (!await EnsureReadyAsync())
            {
                return false;
            }

            IsBusy = true;
            try
            {
                await call();
                OnChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                return Fail(failureMessage, e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static void OnRelationshipAdded(IRelationshipAddedEvent _) => OnChanged?.Invoke();

        private static void OnRelationshipDeleted(IRelationshipDeletedEvent _) => OnChanged?.Invoke();

        private static void OnPresenceUpdated(IPresenceUpdatedEvent _) => OnChanged?.Invoke();

        /// <summary>
        /// An invite arriving from a friend.
        ///
        /// The message is another player's data, so it is treated as such: a payload that will not
        /// deserialise, or that carries no join code, is dropped rather than raised as an invite to
        /// nowhere. The sender's name is read from the local friends list rather than trusted from the
        /// message body, so the banner cannot be made to display something the sender chose.
        /// </summary>
        private static void OnMessageReceived(IMessageReceivedEvent message)
        {
            InviteMessage invite;
            try
            {
                invite = message.GetAs<InviteMessage>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Ignoring an unreadable message from {message.UserId}: {e.Message}");
                return;
            }

            if (invite == null)
            {
                return;
            }

            string name = "A friend";
            foreach (Relationship friend in Friends)
            {
                if (friend?.Member?.Id == message.UserId)
                {
                    name = NameOf(friend);
                    break;
                }
            }

            if (invite.Kind == KindDecline)
            {
                OnInviteDeclined?.Invoke(name);
                return;
            }

            // An invitation with nowhere to go. Dropped rather than shown, or the player is offered a
            // table that does not exist and finds out by tapping it.
            if (string.IsNullOrWhiteSpace(invite.JoinCode))
            {
                return;
            }

            PendingInvite = new Invite(message.UserId, name, invite.JoinCode.Trim());
            OnChanged?.Invoke();
        }

        private static bool Fail(string message, Exception e)
        {
            LastError = message;
            Debug.LogWarning(e == null ? message : $"{message}: {e.Message}");
            return false;
        }
    }
}
