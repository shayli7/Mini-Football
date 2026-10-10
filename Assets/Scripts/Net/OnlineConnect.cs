using Unity.Netcode;

namespace TableFootball.Net
{
    /// <summary>The steps between "the lobby has two players" and "the match is starting", in order.</summary>
    public enum ConnectStage
    {
        /// <summary>The session has recorded both players; the game connection is not up yet.</summary>
        Found = 0,

        /// <summary>This machine's side of the connection is starting and the other has not answered.</summary>
        Linking = 1,

        /// <summary>The two machines can see each other; the guest is still receiving the table.</summary>
        Building = 2,

        /// <summary>The table is up on both ends; the guest is asking the host to kick off.</summary>
        Ready = 3
    }

    /// <summary>
    /// How far along an online connection is, read from the real thing rather than from a clock: the
    /// netcode's own state and whether the match director has spawned. The connecting screen draws its
    /// loading bar from this, so the bar stops where the connection stops — if it sticks on a step, that
    /// step is what is actually stuck.
    ///
    /// Read-only and stateless: it observes <see cref="NetworkManager"/> and
    /// <see cref="OnlineMatchDirector"/>, and changes nothing. The host never sees <see cref="ConnectStage.Building"/>:
    /// its director spawns when it starts hosting, so once the guest is linked the table is already there
    /// and it is only waiting for the guest's ready report.
    /// </summary>
    public static class OnlineConnect
    {
        public static ConnectStage Stage
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening)
                {
                    return ConnectStage.Found;
                }

                bool linked = manager.IsHost
                    ? manager.ConnectedClientsIds.Count >= 2
                    : manager.IsConnectedClient;

                if (!linked)
                {
                    return ConnectStage.Linking;
                }

                return OnlineMatchDirector.IsTableSynced ? ConnectStage.Ready : ConnectStage.Building;
            }
        }
    }
}
