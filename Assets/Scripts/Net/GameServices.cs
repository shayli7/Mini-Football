using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Brings Unity Gaming Services up and signs the player in anonymously.
    ///
    /// Lobby and Relay both refuse every call until the player holds an access token, so this has to
    /// finish before any online menu can do anything. It is deliberately the only place that touches
    /// <see cref="UnityServices"/> or <see cref="AuthenticationService"/> — the lobby and transport
    /// code awaits <see cref="EnsureSignedInAsync"/> instead of initialising anything itself.
    ///
    /// Failure is normal here, not exceptional: a phone with no signal boots exactly like one with a
    /// dead UGS project. So nothing throws out of this class. It records <see cref="Status"/>, and the
    /// local game — which needs none of this — carries on regardless.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameServices : MonoBehaviour
    {
        public enum State
        {
            Idle,
            Working,
            SignedIn,
            Failed
        }

        private static GameServices instance;
        private static Task<bool> inFlight;

        public static State Status { get; private set; } = State.Idle;

        /// <summary>Why the last attempt failed, for the menu to show. Empty unless <see cref="Status"/> is Failed.</summary>
        public static string LastError { get; private set; } = string.Empty;

        /// <summary>Raised on the main thread whenever <see cref="Status"/> changes.</summary>
        public static event Action<State> OnStatusChanged;

        public static bool IsSignedIn =>
            Status == State.SignedIn
            && AuthenticationService.Instance != null
            && AuthenticationService.Instance.IsSignedIn;

        /// <summary>The UGS player id, needed to identify this player to the lobby. Empty until signed in.</summary>
        public static string PlayerId =>
            IsSignedIn ? AuthenticationService.Instance.PlayerId : string.Empty;

        /// <summary>
        /// Creates the object if the scene has none. Called by whoever needs services first, so that a
        /// build that never goes online never spins any of this up.
        /// </summary>
        public static GameServices Ensure()
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<GameServices>();
            }

            if (instance == null)
            {
                instance = new GameObject("GameServices").AddComponent<GameServices>();
            }

            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Initialises UGS and signs in anonymously, returning true once the player has a token.
        ///
        /// Safe to call from anywhere, any number of times: concurrent callers all await the one
        /// attempt, and a successful sign-in short-circuits. A previous *failure* does not — a retry
        /// is the whole point when the last one lost the network.
        /// </summary>
        public static Task<bool> EnsureSignedInAsync()
        {
            if (IsSignedIn)
            {
                return Task.FromResult(true);
            }

            // A call already running is the one to wait on. Starting a second sign-in races the first
            // and can leave the SDK holding two sessions.
            if (inFlight != null && !inFlight.IsCompleted)
            {
                return inFlight;
            }

            Ensure();
            inFlight = SignInAsync();
            return inFlight;
        }

        private static async Task<bool> SignInAsync()
        {
            SetStatus(State.Working, string.Empty);

            try
            {
                // Idempotent, but not free, and it is legal to call before the first frame — which
                // matters because the boot loading screen runs at timeScale 0 and would stall a
                // coroutine. Awaited Tasks are unaffected by timeScale.
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync();
                }

                // The SDK caches a session token on the device, so a returning player keeps the same
                // PlayerId — signing in again when already signed in would throw.
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                SetStatus(State.SignedIn, string.Empty);
                Debug.Log($"UGS signed in anonymously. PlayerId: {AuthenticationService.Instance.PlayerId}");
                return true;
            }
            catch (Exception e)
            {
                // Everything lands here on purpose. The two the SDK actually throws —
                // RequestFailedException for a rejected call and AuthenticationException for a bad
                // session — need the same answer as an unexpected one: stay offline and say so.
                SetStatus(State.Failed, e.Message);
                Debug.LogWarning($"UGS sign-in failed, staying offline: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Drops the cached session and its player id. Only for a deliberate "play as someone else" —
        /// signing out does not help a failed sign-in, and the anonymous id is the player's only
        /// identity, so clearing it is not recoverable.
        /// </summary>
        public static void SignOut(bool clearCredentials = false)
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                return;
            }

            AuthenticationService.Instance.SignOut(clearCredentials);
            SetStatus(State.Idle, string.Empty);
        }

        private static void SetStatus(State next, string error)
        {
            LastError = error;

            if (Status == next)
            {
                return;
            }

            Status = next;
            OnStatusChanged?.Invoke(next);
        }
    }
}
