using TableFootball.Net;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// The coin balance as a chip: the gold coin and the number. Follows <see cref="Wallet.OnChanged"/>,
    /// counting up when the balance rises — see <see cref="UiCounterChip"/> — and is what
    /// <see cref="RewardFly"/> flies coins to.
    ///
    /// The UI Toolkit counterpart of the uGUI <c>CoinPill</c>.
    /// </summary>
    public class UiCoinChip : UiCounterChip
    {
        public UiCoinChip() : base(new UiCoin()) { }

        public override int Target => Wallet.Coins;

        protected override void Follow(bool on)
        {
            Wallet.OnChanged -= Changed;
            if (on) Wallet.OnChanged += Changed;
        }
    }
}
