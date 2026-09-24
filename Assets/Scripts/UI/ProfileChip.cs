using TableFootball.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The compact player-identity chip: avatar, name, a level pill, and an XP bar — the "this is
    /// you" corner of the main menu.
    ///
    /// A self-contained, self-refreshing component so identity lives in one place and any screen that
    /// wants it drops one in. The name is real (from <see cref="PlayerAccount"/>); the level and XP
    /// are visual placeholders (from <see cref="PlayerProgress"/>) until a real progression system
    /// exists — the split is deliberate and the two sources are read exactly the same way, so wiring
    /// the mock half to real data later touches only <see cref="PlayerProgress"/>.
    ///
    /// It subscribes to both sources' change events and redraws itself, the way
    /// <see cref="MainMenu.RefreshAvatar"/> already follows the account — a rename on the profile
    /// screen updates this chip without the caller arranging it.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProfileChip : MonoBehaviour
    {
        private GameObject avatarHolder;
        private TextMeshProUGUI nameText;
        private GameObject badgeHolder;
        private LayoutElement badgeLayout;
        private TextMeshProUGUI levelText;
        private UIFillBar xpFill;
        private TextMeshProUGUI xpLabel;
        private float lastXpFraction = -1f;

        /// <summary>Which league's badge is currently drawn, so <see cref="Refresh"/> can leave it
        /// alone when nothing about it changed. Null means none is drawn.</summary>
        private League? shownBadge;

        // One place the chip's proportions come from. Everything below is measured off these, so the
        // chip can be resized by changing them rather than by re-tuning six separate numbers that
        // were quietly tuned against each other.
        private const float AvatarSize = 72f;
        private const float NameHeight = 40f;
        // 84, down from 96: "LV 60" (the widest it ever shows) fits comfortably, and the 12 units
        // reclaimed go to the name beside it — part of de-crowding the row the badge, pill and name
        // share.
        private const float PillWidth = 84f;
        private const float PillHeight = 30f;
        private const float XpHeight = 26f;
        // The badge draws seven parts inside its face — two handle rings with holes punched through,
        // a bowl, a stem, a base — at native detail tuned for a ~32-unit box (see LeagueBadgeGlyph).
        // At the old 30 that fine detail had nowhere to resolve and read as a smeared blob rather
        // than a trophy; 38 gives it real room without outgrowing the pill beside it. NameHeight
        // grows to match so the row that holds it is tall enough not to clip it — the avatar (72)
        // stays the tallest thing in the chip either way, so nothing else needs to move.
        private const float BadgeSize = 38f;

        public void Build(Transform parent)
        {
            var root = gameObject;
            root.transform.SetParent(parent, false);
            // Fills the holder the caller sized and placed, so the layout group below has a width to
            // divide between the avatar and the text.
            UIFactory.Stretch(UIFactory.Rt(root));

            var row = root.AddComponent<HorizontalLayoutGroup>();
            row.spacing = ArcadeTheme.Md;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = true;
            row.childControlHeight = true;

            // Inset so the avatar, name and bar sit inside the body added below rather than against
            // its edge.
            row.padding = new RectOffset(16, 20, 10, 10);

            // A body for the chip. Without one, the avatar, name and XP bar floated loose on the
            // scrim and read as three stray elements rather than one "you" — the "hidden and ugly"
            // header the menu opened with. A rounded slab with the standard soft shadow gives them a
            // home. Built as an ignored-by-layout child pinned behind the row, so it frames the
            // content without being counted as one of the columns. First sibling, so it draws under.
            var body = UIFactory.Panel(root.transform, "ChipBody");
            body.AddComponent<LayoutElement>().ignoreLayout = true;
            UIFactory.Stretch(UIFactory.Rt(body), 0);
            body.transform.SetAsFirstSibling();

            // A near-black edge instead of the panel's usual grey hairline. The chip sits on menu art
            // rather than on a flat backdrop, and a dark rim is what separates it from whatever colour
            // happens to be behind it — the same trick the reference art uses.
            var border = body.transform.Find("Border");
            if (border != null)
            {
                border.GetComponent<Image>().color = ArcadeTheme.BgDeep;
            }

            // A second, tighter shadow hugging the body. The panel's own is wide and faint — right for
            // a large panel lifting off a backdrop, far too diffuse to read on a small chip, which is
            // why the chip still looked pasted on rather than seated in the art. This one is close and
            // much darker, so the chip casts a real shadow onto the menu behind it.
            var shadow = UIFactory.Child(root.transform, "ChipShadow");
            shadow.AddComponent<LayoutElement>().ignoreLayout = true;
            UIFactory.GlowImage(shadow, ArcadeTheme.RadLg, 18f, Color.black.WithAlpha(0.62f));
            UIFactory.Stretch(UIFactory.Rt(shadow), -6f, -9f, -6f, -3f);
            // Behind the body, which is itself behind the content.
            shadow.transform.SetAsFirstSibling();

            avatarHolder = UIFactory.Child(root.transform, "AvatarHolder");
            var ale = avatarHolder.AddComponent<LayoutElement>();
            ale.preferredWidth = AvatarSize;
            ale.preferredHeight = AvatarSize;
            ale.minWidth = AvatarSize;

            // Name on top, level + XP stacked under it.
            var col = UIFactory.Child(root.transform, "Text");
            var cle = col.AddComponent<LayoutElement>();
            cle.preferredWidth = 240f;
            cle.flexibleWidth = 1f;
            var cv = col.AddComponent<VerticalLayoutGroup>();
            cv.spacing = ArcadeTheme.Xs;
            cv.childAlignment = TextAnchor.MiddleLeft;
            cv.childForceExpandWidth = true;
            cv.childForceExpandHeight = false;
            cv.childControlWidth = true;
            cv.childControlHeight = true;

            // Name row: the name, then a level pill hard against its right.
            var nameRow = UIFactory.Child(col.transform, "NameRow");
            nameRow.AddComponent<LayoutElement>().preferredHeight = NameHeight;
            var nh = nameRow.AddComponent<HorizontalLayoutGroup>();
            nh.spacing = ArcadeTheme.Sm;
            nh.childAlignment = TextAnchor.MiddleLeft;
            nh.childForceExpandWidth = false;
            nh.childForceExpandHeight = false;
            nh.childControlWidth = true;
            nh.childControlHeight = true;

            // Not rich text: it is the player's own chosen name, and one stray tag would rewrite the
            // whole chip's layout.
            nameText = UIFactory.Text(nameRow.transform, "—", ArcadeTheme.FsBody * 1.1f, ArcadeTheme.Ink,
                                      display: true, bold: true, upper: false, tracking: 1f,
                                      align: TextAlignmentOptions.Left, richText: false);
            var nle = nameText.gameObject.AddComponent<LayoutElement>();
            nle.preferredWidth = 0f;
            nle.flexibleWidth = 1f;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.enableWordWrapping = false;

            BuildBadgeSlot(nameRow.transform);
            BuildLevelPill(nameRow.transform);

            // XP bar with its own count riding on it.
            var xp = UIFactory.XpBar(col.transform, PlayerProgress.XpFraction,
                                     PlayerProgress.XpLabel, XpHeight);
            xpFill = xp.GetComponentInChildren<UIFillBar>();
            xpLabel = xp.GetComponentInChildren<TextMeshProUGUI>();

            Refresh();

            PlayerAccount.OnChanged += Refresh;
            PlayerProgress.OnChanged += Refresh;
            // The badge beside the name is an equipped cosmetic like any other, so the chip follows
            // the inventory for the same reason it follows the account: equipping a different badge on
            // another screen has to be reflected here without that screen arranging it.
            Inventory.OnChanged += Refresh;
        }

        /// <summary>
        /// The slot the league badge sits in, immediately right of the name — "next to your player",
        /// which is the only place a badge earned for a rank means anything.
        ///
        /// Built empty and filled by <see cref="Refresh"/>, because a badge is not a permanent part of
        /// the chip: a new player has none, and the one worn changes as the ladder is climbed. The
        /// cell collapses to zero width when there is nothing to show, so a chip with no badge is the
        /// chip exactly as it was before badges existed rather than one with a gap in it.
        /// </summary>
        private void BuildBadgeSlot(Transform parent)
        {
            badgeHolder = UIFactory.Child(parent, "Badge");
            badgeLayout = badgeHolder.AddComponent<LayoutElement>();
            badgeLayout.preferredHeight = BadgeSize;
            badgeLayout.preferredWidth = 0f;
            badgeLayout.minWidth = 0f;
        }

        private void BuildLevelPill(Transform parent)
        {
            var pill = UIFactory.Child(parent, "LevelPill");
            var le = pill.AddComponent<LayoutElement>();
            le.preferredWidth = PillWidth;
            le.minWidth = PillWidth;
            le.preferredHeight = PillHeight;

            UIFactory.RoundedImage(pill, ArcadeTheme.RadSm, ArcadeTheme.Gold.WithAlpha(0.16f), false);

            levelText = UIFactory.Text(pill.transform, "LV —", ArcadeTheme.FsCaption * 0.92f,
                                       ArcadeTheme.Gold, display: true, bold: true, upper: true,
                                       tracking: 3f);
            UIFactory.Stretch(UIFactory.Rt(levelText.gameObject), 0);
        }

        private void Refresh()
        {
            if (nameText == null) return;

            string name = PlayerAccount.DisplayName;
            nameText.text = string.IsNullOrWhiteSpace(name) ? "—" : name;

            if (levelText != null) levelText.text = $"LV {PlayerProgress.Level}";

            RefreshBadge();

            // Rebuilt rather than re-tinted: the disc bakes the initial in at build time, so a rename
            // needs a fresh one. Cheap — one image and a letter — and it is the same thing the friends
            // screen does with its own identity row.
            if (avatarHolder != null)
            {
                UIFactory.ClearChildren(avatarHolder.transform);
                UIFactory.AvatarDisc(avatarHolder.transform, name, ArcadeTheme.Gold, AvatarSize);
            }

            if (xpLabel != null) xpLabel.text = PlayerProgress.XpLabel;

            if (xpFill != null)
            {
                float fraction = PlayerProgress.XpFraction;

                // Sweep from wherever the bar last sat rather than always from empty: XP gained
                // within the same level should visibly GROW the bar. A level-up wraps the fraction
                // back to a lower value on a fresh band, and only then does an empty-in sweep make
                // sense again — the same as the bar's very first appearance.
                float from = lastXpFraction >= 0f && fraction >= lastXpFraction ? lastXpFraction : 0f;
                xpFill.SetFraction(fraction, from);
                lastXpFraction = fraction;
            }
        }

        /// <summary>
        /// Draws whichever league badge the player has equipped, or collapses the slot when none is.
        ///
        /// Rebuilt only when the badge actually CHANGES, unlike the avatar above — the avatar is one
        /// image and a letter, but a badge is a ring, a face and a seven-part cup, and this method runs
        /// on every account, progression and inventory event the chip listens to. Rebuilding it each
        /// time would throw away and re-create ten objects because the player's XP moved.
        /// </summary>
        private void RefreshBadge()
        {
            if (badgeHolder == null)
            {
                return;
            }

            CosmeticItem worn = LeagueBadges.Worn;
            League? league = worn.Valid ? LeagueBadges.LeagueOf(worn.Id) : (League?)null;

            if (league == shownBadge)
            {
                return;
            }

            shownBadge = league;
            UIFactory.ClearChildren(badgeHolder.transform);

            float width = league.HasValue ? BadgeSize : 0f;
            badgeLayout.preferredWidth = width;
            badgeLayout.minWidth = width;

            if (league.HasValue)
            {
                UIFactory.LeagueBadgeGlyph(badgeHolder.transform, league.Value, BadgeSize);
            }
        }

        private void OnDestroy()
        {
            PlayerAccount.OnChanged -= Refresh;
            PlayerProgress.OnChanged -= Refresh;
            Inventory.OnChanged -= Refresh;
        }
    }
}
