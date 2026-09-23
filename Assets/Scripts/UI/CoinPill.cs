using System.Collections;
using TableFootball.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The coin balance, in a little bordered pill — the currency's one permanent home on screen.
    ///
    /// A self-contained, self-refreshing component in the manner of <see cref="ProfileChip"/>: it
    /// subscribes to <see cref="Wallet.OnChanged"/> and redraws itself, so any screen that wants the
    /// balance drops one in and nothing has to remember to update it. That matters more here than it
    /// does for the chip — coins move from four different places (a level reward, a chest, the weekly
    /// ranked payout, a purchase), and a pill that had to be told would eventually be told by three of
    /// them and not the fourth.
    ///
    /// It counts UP to a gain rather than snapping. A number that silently changes while the player is
    /// looking at something else is a reward they did not see arrive, which is the same problem the
    /// level path solves by making rewards claimable.
    /// </summary>
    [DisallowMultipleComponent]
    public class CoinPill : MonoBehaviour
    {
        private TextMeshProUGUI amount;
        private Transform body;
        private Coroutine anim;

        /// <summary>What the pill is currently SHOWING, which is not the balance while a count-up is
        /// running. Starts at -1 so the first draw snaps rather than counting up from zero.</summary>
        private int shown = -1;

        /// <summary>
        /// Fills <paramref name="parent"/> with the pill. The caller sizes and places the holder; this
        /// stretches to it, so the pill is exactly as tall as the row it was given — the same contract
        /// <see cref="ProfileChip.Build"/> works to, and what keeps the two on one plane in the main
        /// menu header.
        /// </summary>
        public void Build(Transform parent)
        {
            var root = gameObject;
            root.transform.SetParent(parent, false);
            UIFactory.Stretch(UIFactory.Rt(root));

            // The pill body: a raised slab with a coin-gold rim. Gold rather than the usual grey
            // hairline because the border IS the thing that makes this read as currency rather than as
            // another stat — and it has to hold its own beside the profile chip, which is bigger and
            // brighter than it is.
            var slab = UIFactory.Child(root.transform, "PillBody");
            body = slab.transform;
            UIFactory.RoundedImage(slab, ArcadeTheme.RadMd, ArcadeTheme.CoinDark, false);
            UIFactory.Stretch(UIFactory.Rt(slab), 0);

            var fill = UIFactory.Child(slab.transform, "Fill");
            UIFactory.RoundedImage(fill, ArcadeTheme.RadMd, ArcadeTheme.BgRaised, false);
            UIFactory.Stretch(UIFactory.Rt(fill), 2f);

            var row = UIFactory.Child(fill.transform, "Row");
            UIFactory.Stretch(UIFactory.Rt(row), 12f, 0f, 12f, 0f);

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Sm;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            const float coin = 28f;
            var coinCell = UIFactory.Child(row.transform, "CoinCell");
            var cle = coinCell.AddComponent<LayoutElement>();
            cle.preferredWidth = coin;
            cle.minWidth = coin;
            cle.preferredHeight = coin;
            UIFactory.CoinGlyph(coinCell.transform, coin);

            amount = UIFactory.Text(row.transform, "0", ArcadeTheme.FsBody, ArcadeTheme.Coin,
                                    display: true, bold: true, upper: false, tracking: 1f,
                                    align: TextAlignmentOptions.Left, richText: false);
            amount.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Refresh();

            Wallet.OnChanged += Refresh;
        }

        private void Refresh()
        {
            if (amount == null)
            {
                return;
            }

            int balance = Wallet.Coins;
            if (balance == shown)
            {
                return;
            }

            // Three cases snap rather than count. A spend, because watching a balance tick DOWN to
            // what you just paid draws the eye to the cost rather than to the thing bought. The very
            // first draw, which has nothing to count from. And a pill on a CLOSED screen — every pill
            // in the game stays subscribed while its menu is hidden, and StartCoroutine on an inactive
            // GameObject is a Unity error, not a no-op. That last one is not hypothetical: claiming a
            // reward on the level path credits coins while the main menu's own pill is switched off.
            if (shown < 0 || balance < shown || !isActiveAndEnabled)
            {
                shown = balance;
                amount.text = balance.ToString();
                return;
            }

            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(CountTo(balance));
        }

        private IEnumerator CountTo(int target)
        {
            int from = shown;
            shown = target;

            if (body != null) StartCoroutine(UITween.Pop(body, 1.12f, ArcadeTheme.TNormal));
            yield return UITween.CountUp(amount, from, target, ArcadeTheme.TSlow);

            // Left exact rather than trusted to land there: CountUp interpolates, and a run cut short
            // by a second gain arriving mid-count would otherwise leave the pill a few coins light.
            amount.text = target.ToString();
            anim = null;
        }

        private void OnDestroy()
        {
            Wallet.OnChanged -= Refresh;
        }
    }
}
