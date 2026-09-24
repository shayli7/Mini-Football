namespace TableFootball
{
    /// <summary>
    /// A snapshot of where the local player sits in the ladder right now — everything the League
    /// screen and the profile badge need to draw, resolved by whichever backend is active. A value
    /// type: cheap to cache and hand around.
    /// </summary>
    public struct LadderStanding
    {
        public bool Valid;          // false before the first successful read (draw a placeholder)
        public League League;
        public int PodId;
        public int RankInPod;       // 1 = top of the pod
        public int PodSize;
        public int WeeklyPoints;
        public bool InPromotionZone;   // this rank promotes at rollover (and the league can promote)
        public bool InRelegationZone;  // this rank relegates at rollover (and the league can relegate)
        public int WeekIndex;
        public long SecondsToRollover; // until the pod locks and promote/relegate resolves
    }

    /// <summary>One row of a pod's standings — a rival (or you) as shown on the League screen.</summary>
    public struct LadderEntry
    {
        public string Name;
        public int Points;
        public bool IsYou;
    }
}
