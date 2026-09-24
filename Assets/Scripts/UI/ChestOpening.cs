using System;
using System.Collections.Generic;
using TableFootball.Net;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace TableFootball.UI
{
    /// <summary>
    /// The chest opening: a full-screen overlay that plays whenever a chest pays out.
    ///
    /// The sequence has four beats. The chest DROPS in and lands with a squash. It then waits,
    /// bobbing and rattling now and then, with light leaking from under the lid, until the player taps
    /// it. The tap CHARGES it: the rattle builds, the glow swells and the lid strains upward, for
    /// longer the better the chest. Then it BURSTS: a flash, the lid flies off, sparks, rays in the
    /// chest's colour, and the prize rises out of the chest with its name beneath it.
    ///
    /// The roll has already happened by the time this plays. <see cref="ChestLoot.Open"/> grants the
    /// drop before anything is drawn, so what the overlay shows is always what the player owns, and
    /// closing the app mid-animation loses nothing. This is presentation only.
    ///
    /// Everything is driven from one per-frame loop on unscaled time rather than USS transitions,
    /// because the beats chain into each other and wait on a tap, which transitions cannot. Built
    /// once, the first time a chest opens, on the shared UI Toolkit panel; reduced motion skips
    /// straight to the prize.
    /// </summary>
    public static class ChestOpening
    {
        private enum Phase { Hidden, Drop, Idle, Charge, Burst }

        // Beat lengths, in seconds.
        private const float DropTime = 0.4f;
        private const float SettleTime = 0.55f;
        private const float ChargeBase = 0.6f;
        private const float ChargePerTier = 0.14f;

        private static VisualElement root;
        private static VisualElement stage;
        private static VisualElement rays;
        private static VisualElement glow;
        private static VisualElement chest;
        private static VisualElement innerLight;
        private static ChestPart body;
        private static ChestPart lid;
        private static VisualElement sparkLayer;
        private static VisualElement card;
        private static VisualElement cardPicture;
        private static VisualElement flash;
        private static VisualElement info;
        private static VisualElement collectRow;
        private static Label kicker;
        private static Label prompt;
        private static Label nameLabel;
        private static Label detailLabel;
        private static Label noteLabel;
        private static IVisualElementScheduledItem loop;

        private static Phase phase = Phase.Hidden;
        private static float phaseStart;
        private static bool landed;
        private static bool infoShown;
        private static ChestTier tier;
        private static Color tint;
        private static Action onClosed;

        private class Spark
        {
            public VisualElement E;
            public Vector2 Pos;
            public Vector2 Vel;
            public float Spin;
            public float Life;
            public float Age;
        }

        private static readonly List<Spark> sparks = new List<Spark>();

        public static bool IsOpen => root != null && phase != Phase.Hidden;

        /// <summary>
        /// Plays the opening for a chest that has already been rolled. <paramref name="item"/> is the
        /// skin it gave, or invalid when it paid <paramref name="coins"/> instead.
        /// <paramref name="closed"/> runs once, when the player collects.
        /// </summary>
        public static void Play(ChestTier chestTier, CosmeticItem item, int coins, Action closed)
        {
            Build();

            tier = chestTier;
            tint = UIFactory.ChestColor(chestTier);
            onClosed = closed;

            body.Tint = lid.Tint = tint;
            body.MarkDirtyRepaint();
            lid.MarkDirtyRepaint();
            innerLight.style.backgroundColor = Color.Lerp(tint, Color.white, 0.55f);
            glow.style.unityBackgroundImageTintColor = Color.Lerp(tint, ArcadeTheme.Gold, 0.25f);
            rays.style.unityBackgroundImageTintColor = Color.Lerp(tint, Color.white, 0.2f);

            kicker.text = ChestLoot.TierName(chestTier).ToUpperInvariant();
            kicker.style.color = tint;

            FillPrize(item, coins);
            ResetPoses();

            root.style.opacity = 0f;
            UiKit.Show(root, true);
            root.BringToFront();
            UiKit.Fade(root, 1f, 0.2f);

            if (ArcadeTheme.ReducedMotion)
            {
                ShowPrizeAtRest();
                phase = Phase.Burst;
                phaseStart = Time.unscaledTime - 10f;
            }
            else
            {
                Begin(Phase.Drop);
            }

            loop.Resume();
        }

        /// <summary>Hides the overlay at once without running the close callback — for a screen that
        /// is being closed out from under it.</summary>
        public static void Cancel()
        {
            if (root == null) return;
            Hide();
            onClosed = null;
        }

        // ---------- build ----------

        private static void Build()
        {
            if (root != null) return;

            root = UiKit.El("screen chest-open", UiToolkitHost.Root, "ChestOpening");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/ChestOpening");
            if (sheet != null) root.styleSheets.Add(sheet);
            // Blocks the table and anything beneath while it is up.
            root.pickingMode = PickingMode.Position;
            UiKit.OnTap(root, Tapped);

            UiKit.El("bleed chest-open__scrim", root).pickingMode = PickingMode.Ignore;

            kicker = UiKit.Text(string.Empty, "chest-open__kicker f-display", root);

            stage = UiKit.El("chest-open__stage", root);
            stage.pickingMode = PickingMode.Ignore;

            rays = UiKit.El("chest-open__rays", stage);
            rays.style.backgroundImage = new StyleBackground(RaysTexture());

            glow = UiKit.El("chest-open__glow", stage);
            glow.style.backgroundImage = new StyleBackground(GlowTexture());

            chest = UiKit.El("chest-open__chest", stage);
            innerLight = UiKit.El("chest-open__inner", chest);
            body = new ChestPart(false);
            body.AddToClassList("chest-open__body");
            chest.Add(body);
            lid = new ChestPart(true);
            lid.AddToClassList("chest-open__lid");
            chest.Add(lid);

            card = UiKit.El("chest-open__card", stage);
            cardPicture = UiKit.El("chest-open__picture", card);

            sparkLayer = UiKit.El("chest-open__sparks", stage);

            prompt = UiKit.Text("TAP TO OPEN", "chest-open__prompt f-display-semi", root);

            info = UiKit.El("chest-open__info", root);
            nameLabel = UiKit.Text(string.Empty, "chest-open__name f-display", info);
            detailLabel = UiKit.Text(string.Empty, "chest-open__detail f-body-semi", info);
            noteLabel = UiKit.Text(string.Empty, "chest-open__note f-body-medium", info);

            // The button sits in a row of its own so the entrance transition on the row does not
            // replace the button's own press transitions.
            collectRow = UiKit.El("chest-open__collect", root);
            UiKit.Button("COLLECT", "gold", Collect, collectRow);

            flash = UiKit.El("bleed chest-open__flash", root);
            flash.pickingMode = PickingMode.Ignore;

            UiFonts.Apply(root);
            UiKit.Show(root, false);

            loop = UiKit.Loop(root, Tick);
            loop.Pause();
        }

        /// <summary>The prize card's picture and the three lines under it.</summary>
        private static void FillPrize(CosmeticItem item, int coins)
        {
            cardPicture.Clear();

            if (item.Valid)
            {
                Color rarity = UIFactory.RarityColor(item.Rarity);
                SetBorder(card, rarity);
                cardPicture.style.backgroundColor = Color.Lerp(ArcadeTheme.BgDeep, rarity, 0.18f);

                Sprite thumb = CosmeticThumbnails.For(item.Id);
                if (thumb != null)
                {
                    var art = UiKit.El("chest-open__art", cardPicture);
                    art.style.backgroundImage = new StyleBackground(thumb);
                }
                else
                {
                    var icon = new UiIcon(StoreMenu.KindGlyph(item.Kind), rarity, 1.6f);
                    icon.AddToClassList("chest-open__glyph");
                    cardPicture.Add(icon);
                }

                nameLabel.text = item.Name;
                detailLabel.text = ($"{CosmeticCatalog.RarityName(item.Rarity)} " +
                                    $"{CosmeticCatalog.KindLabel(item.Kind)}").ToUpperInvariant();
                detailLabel.style.color = rarity;
                noteLabel.text = "New — equip it from the store";
            }
            else
            {
                SetBorder(card, ArcadeTheme.Gold);
                cardPicture.style.backgroundColor = Color.Lerp(ArcadeTheme.BgDeep, ArcadeTheme.Gold, 0.14f);
                var icon = new UiIcon(UiIcon.Glyph.Coin, ArcadeTheme.Gold, 1.6f);
                icon.AddToClassList("chest-open__glyph");
                cardPicture.Add(icon);

                nameLabel.text = $"{coins} coins";
                detailLabel.text = "ADDED TO YOUR WALLET";
                detailLabel.style.color = ArcadeTheme.Gold;
                // A chest with nothing left to give is not a bad roll, and a player who is not told
                // that will read it as one.
                noteLabel.text = "Every skin in this chest is already yours";
            }
        }

        // ---------- the sequence ----------

        private static void Begin(Phase next)
        {
            phase = next;
            phaseStart = Time.unscaledTime;
        }

        private static void Tapped()
        {
            // Only the waiting chest answers a tap. Once it has burst, the button is the way out, so a
            // stray second tap cannot dismiss the prize before the player has read it.
            if (phase == Phase.Drop || phase == Phase.Idle)
            {
                prompt.style.opacity = 0f;
                chest.style.translate = new Translate(0f, 0f);
                Begin(Phase.Charge);
            }
        }

        private static void Tick(float now)
        {
            float t = now - phaseStart;

            switch (phase)
            {
                case Phase.Drop: TickDrop(t); break;
                case Phase.Idle: TickIdle(t); break;
                case Phase.Charge: TickCharge(t); break;
                case Phase.Burst: TickBurst(t, now); break;
            }
        }

        /// <summary>Falls in from above, lands with a squash and a thud, and settles.</summary>
        private static void TickDrop(float t)
        {
            glow.style.opacity = Mathf.Clamp01(t / (DropTime + SettleTime)) * 0.35f;

            if (t < DropTime)
            {
                float k = t / DropTime;
                Pose(chest, 0f, Mathf.Lerp(-520f, 0f, k * k), 1f, 1f, 0f);
                return;
            }

            if (!landed)
            {
                landed = true;
                GameSfx.PlayWallThud(1f);
            }

            // A damped spring about rest, wide and short on landing then tall and narrow.
            float s = t - DropTime;
            float squash = Mathf.Exp(-s * 8f) * Mathf.Cos(s * 26f) * 0.17f;
            Pose(chest, 0f, 0f, 1f + squash, 1f - squash, 0f);

            if (s >= SettleTime)
            {
                Pose(chest, 0f, 0f, 1f, 1f, 0f);
                Begin(Phase.Idle);
            }
        }

        /// <summary>Waiting for the tap: a slow bob, a rattle every couple of seconds, light under the
        /// lid, and the prompt breathing.</summary>
        private static void TickIdle(float t)
        {
            float bob = Mathf.Sin(t * 2.4f) * 4f;

            float cycle = t % 1.9f;
            float rattle = cycle < 0.4f ? Mathf.Sin(cycle * 55f) * 4.5f * (1f - cycle / 0.4f) : 0f;
            float lift = cycle < 0.4f ? Mathf.Abs(Mathf.Sin(cycle * 55f)) * 4f * (1f - cycle / 0.4f) : 0f;

            Pose(chest, 0f, bob, 1f, 1f, rattle);
            Pose(lid, 0f, -lift, 1f, 1f, 0f);
            innerLight.style.opacity = 0.35f + lift * 0.15f;
            glow.style.opacity = 0.32f + Mathf.Sin(t * 3f) * 0.06f;

            float breath = Mathf.Sin(t * 3.4f);
            prompt.style.opacity = Mathf.Clamp01(t * 3f) * (0.6f + 0.4f * breath * breath);
        }

        /// <summary>The build-up after the tap: the shake grows, the lid strains, the glow swells.
        /// Better chests take longer, which is most of what makes a Legendary feel like one.</summary>
        private static void TickCharge(float t)
        {
            float length = ChargeBase + ChargePerTier * (int)tier;
            float k = Mathf.Clamp01(t / length);
            float amp = Mathf.Lerp(2f, 11f, k * k);

            float shake = Mathf.Sin(t * 70f) * amp;
            float grow = 1f + 0.1f * k;
            Pose(chest, Mathf.Sin(t * 53f) * amp * 0.4f, 0f, grow, grow, shake);
            Pose(lid, 0f, -(3f + 12f * k * k) - Mathf.Abs(Mathf.Sin(t * 61f)) * 4f * k, 1f, 1f, 0f);

            innerLight.style.opacity = 0.4f + 0.6f * k;
            glow.style.opacity = 0.35f + 0.65f * k;
            float g = 1f + 0.45f * k;
            glow.style.scale = new Scale(new Vector2(g, g));

            if (t >= length) Burst();
        }

        private static void Burst()
        {
            Begin(Phase.Burst);
            GameSfx.PlayQuestComplete((int)tier);

            Pose(chest, 0f, 0f, 1f, 1f, 0f);
            flash.style.opacity = 0.9f;
            SpawnSparks(18 + 10 * (int)tier);
        }

        /// <summary>The payoff, and then the prize at rest with the rays still turning behind it.</summary>
        private static void TickBurst(float t, float now)
        {
            flash.style.opacity = Mathf.Max(0f, 0.9f * (1f - t / 0.45f));

            // The lid is thrown up and off to one side, turning, and fades as it goes.
            if (t < 0.8f)
            {
                Pose(lid, -300f * t, -14f - 1000f * t + 900f * t * t, 1f, 1f, -240f * t);
                lid.style.opacity = Mathf.Clamp01(1f - t / 0.6f);
            }
            else
            {
                lid.style.opacity = 0f;
            }

            // The chest kicks, then sinks away to leave the prize the centre of the screen.
            if (t < 0.1f)
            {
                Pose(body, 0f, 0f, 1.12f, 0.88f, 0f);
            }
            else
            {
                float k = ArcadeTheme.EaseOut(Mathf.Clamp01((t - 0.1f) / 0.5f));
                Pose(body, 0f, 70f * k, 1f - 0.25f * k, 1f - 0.25f * k, 0f);
                body.style.opacity = 1f - k;
            }
            innerLight.style.opacity = Mathf.Clamp01(1f - t / 0.3f);

            float rayIn = ArcadeTheme.EaseOut(Mathf.Clamp01(t / 0.5f));
            rays.style.opacity = 0.9f * rayIn;
            float rs = 0.5f + 0.5f * rayIn;
            rays.style.scale = new Scale(new Vector2(rs, rs));
            rays.style.rotate = new Rotate(new Angle(now * 14f % 360f, AngleUnit.Degree));

            glow.style.opacity = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(t / 0.8f));

            // The prize rises out of the chest, overshoots a touch and settles, then floats.
            float c = Mathf.Clamp01((t - 0.08f) / 0.6f);
            float pop = Mathf.Lerp(0.2f, 1f, BackOut(c));
            float rise = Mathf.Lerp(80f, 0f, ArcadeTheme.EaseOut(c)) + (c >= 1f ? Mathf.Sin(now * 2f) * 4f : 0f);
            Pose(card, 0f, rise, pop, pop, 0f);
            card.style.opacity = Mathf.Clamp01(c * 3f);

            TickSparks(Time.unscaledDeltaTime);

            if (!infoShown && t > 0.45f)
            {
                infoShown = true;
                UiKit.Show(info, true);
                UiKit.Show(collectRow, true);
                UiKit.Enter(info);
                UiKit.Enter(collectRow, 0.25f);
            }
        }

        private static void ShowPrizeAtRest()
        {
            chest.style.opacity = 0f;
            rays.style.opacity = 0.9f;
            glow.style.opacity = 0.6f;
            card.style.opacity = 1f;
            Pose(card, 0f, 0f, 1f, 1f, 0f);
            prompt.style.opacity = 0f;
            infoShown = true;
            UiKit.Show(info, true);
            UiKit.Show(collectRow, true);
        }

        private static void Collect()
        {
            if (phase != Phase.Burst) return;
            UiKit.Fade(root, 0f, 0.2f, () =>
            {
                Hide();
                Action done = onClosed;
                onClosed = null;
                done?.Invoke();
            });
        }

        private static void Hide()
        {
            phase = Phase.Hidden;
            loop?.Pause();
            ClearSparks();
            UiKit.Show(root, false);
        }

        /// <summary>Everything back to where the drop starts from.</summary>
        private static void ResetPoses()
        {
            landed = false;
            infoShown = false;
            ClearSparks();

            chest.style.opacity = 1f;
            Pose(chest, 0f, -520f, 1f, 1f, 0f);
            Pose(lid, 0f, 0f, 1f, 1f, 0f);
            lid.style.opacity = 1f;
            Pose(body, 0f, 0f, 1f, 1f, 0f);
            body.style.opacity = 1f;
            innerLight.style.opacity = 0f;

            rays.style.opacity = 0f;
            glow.style.opacity = 0f;
            glow.style.scale = new Scale(Vector2.one);
            card.style.opacity = 0f;
            flash.style.opacity = 0f;
            prompt.style.opacity = 0f;

            UiKit.Show(info, false);
            UiKit.Show(collectRow, false);
        }

        // ---------- sparks ----------

        private static void SpawnSparks(int count)
        {
            Color[] colours = { tint, ArcadeTheme.Gold, Color.Lerp(tint, Color.white, 0.7f) };

            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float speed = Random.Range(360f, 860f);
                float size = Random.Range(6f, 13f);

                var e = UiKit.El("chest-open__spark", sparkLayer);
                e.style.width = size;
                e.style.height = size;
                e.style.marginLeft = -size * 0.5f;
                e.style.marginTop = -size * 0.5f;
                e.style.backgroundColor = colours[i % colours.Length];

                sparks.Add(new Spark
                {
                    E = e,
                    Pos = new Vector2(0f, -30f),
                    // Biased upward, so the burst reads as coming OUT of the chest.
                    Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f - 0.55f) * speed,
                    Spin = Random.Range(-540f, 540f),
                    Life = Random.Range(0.7f, 1.25f)
                });
            }
        }

        private static void TickSparks(float dt)
        {
            dt = Mathf.Min(dt, 0.05f);
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                Spark s = sparks[i];
                s.Age += dt;
                if (s.Age >= s.Life)
                {
                    s.E.RemoveFromHierarchy();
                    sparks.RemoveAt(i);
                    continue;
                }

                s.Vel *= Mathf.Pow(0.18f, dt); // air drag
                s.Vel.y += 900f * dt;           // gravity, in panel units (y runs down)
                s.Pos += s.Vel * dt;

                float k = s.Age / s.Life;
                float scale = 1f - k * k;
                Pose(s.E, s.Pos.x, s.Pos.y, scale, scale, 45f + s.Spin * s.Age);
                s.E.style.opacity = 1f - k * k;
            }
        }

        private static void ClearSparks()
        {
            foreach (var s in sparks) s.E.RemoveFromHierarchy();
            sparks.Clear();
        }

        // ---------- helpers ----------

        private static void Pose(VisualElement e, float x, float y, float sx, float sy, float degrees)
        {
            e.style.translate = new Translate(x, y);
            e.style.scale = new Scale(new Vector2(sx, sy));
            e.style.rotate = new Rotate(new Angle(degrees, AngleUnit.Degree));
        }

        private static void SetBorder(VisualElement e, Color c)
        {
            e.style.borderTopColor = e.style.borderBottomColor =
                e.style.borderLeftColor = e.style.borderRightColor = c;
        }

        /// <summary>Ease-out with a small overshoot past 1 before settling.</summary>
        private static float BackOut(float t)
        {
            const float c1 = 1.9f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ---------- textures ----------

        private static Texture2D glowTex;
        private static Texture2D raysTex;

        /// <summary>A soft white disc fading to nothing, tinted per chest.</summary>
        private static Texture2D GlowTexture()
        {
            if (glowTex != null) return glowTex;

            const int n = 64;
            glowTex = NewTexture(n, "ChestGlow");
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / 0.5f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            }
            glowTex.SetPixels32(px);
            glowTex.Apply(false, false);
            return glowTex;
        }

        /// <summary>Twelve soft light beams from the centre, fading outward. Tinted per chest and
        /// turned slowly from code.</summary>
        private static Texture2D RaysTexture()
        {
            if (raysTex != null) return raysTex;

            const int n = 256;
            const int beams = 12;
            raysTex = NewTexture(n, "ChestRays");
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / 0.5f;
                    float ang = Mathf.Atan2(dy, dx);
                    // A beam is where the angular wave is high; narrow the band with a power.
                    float beam = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(ang * beams)), 6f);
                    float fade = Mathf.Clamp01(1f - r);
                    float core = Mathf.Clamp01(1f - r / 0.25f);
                    float a = Mathf.Clamp01(beam * fade * fade * 0.55f + core * core * 0.3f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }
            raysTex.SetPixels32(px);
            raysTex.Apply(false, false);
            return raysTex;
        }

        private static Texture2D NewTexture(int n, string name) =>
            new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name
            };

        // ---------- the chest itself ----------

        /// <summary>
        /// The body or the lid of the chest, drawn with the vector API so it stays crisp at any size.
        /// Two elements rather than one so the lid can fly off on its own. Wood in the chest's tier
        /// colour, fittings in gold on every tier — the same reading as the level path's chest glyph.
        /// </summary>
        private sealed class ChestPart : VisualElement
        {
            private readonly bool isLid;
            public Color Tint = Color.gray;

            public ChestPart(bool isLid)
            {
                this.isLid = isLid;
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext ctx)
            {
                Rect r = contentRect;
                if (r.width <= 0f || r.height <= 0f) return;

                var p = ctx.painter2D;
                Color woodDark = Shade(Tint, 0.42f);
                Color wood = Shade(Tint, 0.78f);
                Color trim = ArcadeTheme.Gold;
                Color trimDark = Shade(ArcadeTheme.Gold, 0.72f);

                if (isLid) DrawLid(p, r.width, r.height, woodDark, wood, trim, trimDark);
                else DrawBody(p, r.width, r.height, woodDark, wood, trim, trimDark);
            }

            private static void DrawBody(Painter2D p, float w, float h, Color woodDark, Color wood,
                                         Color trim, Color trimDark)
            {
                Fill(p, woodDark, () => RoundRect(p, 0f, 0f, w, h, 3f, 16f));
                Fill(p, wood, () => RoundRect(p, 12f, 16f, w - 24f, h - 32f, 6f, 8f));

                // Plank line across the front.
                p.strokeColor = woodDark;
                p.lineWidth = 3f;
                p.BeginPath();
                p.MoveTo(new Vector2(14f, h * 0.56f));
                p.LineTo(new Vector2(w - 14f, h * 0.56f));
                p.Stroke();

                Fill(p, trim, () => RoundRect(p, 0f, 0f, w, 12f, 3f, 2f));
                Fill(p, trimDark, () => RoundRect(p, 0f, h - 12f, w, 12f, 2f, 10f));
                foreach (float sx in new[] { 32f, w - 50f })
                {
                    Fill(p, trimDark, () => RoundRect(p, sx, 0f, 18f, h, 2f, 3f));
                    Fill(p, trim, () => RoundRect(p, sx + 3f, 0f, 4f, h - 4f, 1f, 1f));
                }

                // Lock plate and keyhole.
                Fill(p, trim, () => RoundRect(p, w * 0.5f - 22f, 2f, 44f, 50f, 5f, 12f));
                Fill(p, woodDark, () =>
                {
                    p.BeginPath();
                    p.Arc(new Vector2(w * 0.5f, 22f), 6f, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
                    p.ClosePath();
                });
                Fill(p, woodDark, () => RoundRect(p, w * 0.5f - 3f, 24f, 6f, 16f, 1f, 2f));
            }

            private static void DrawLid(Painter2D p, float w, float h, Color woodDark, Color wood,
                                        Color trim, Color trimDark)
            {
                Fill(p, woodDark, () =>
                {
                    p.BeginPath();
                    p.MoveTo(new Vector2(0f, h));
                    p.LineTo(new Vector2(0f, 30f));
                    p.BezierCurveTo(new Vector2(0f, 6f), new Vector2(24f, 0f), new Vector2(56f, 0f));
                    p.LineTo(new Vector2(w - 56f, 0f));
                    p.BezierCurveTo(new Vector2(w - 24f, 0f), new Vector2(w, 6f), new Vector2(w, 30f));
                    p.LineTo(new Vector2(w, h));
                    p.ClosePath();
                });
                Fill(p, wood, () =>
                {
                    p.BeginPath();
                    p.MoveTo(new Vector2(12f, h - 12f));
                    p.LineTo(new Vector2(12f, 32f));
                    p.BezierCurveTo(new Vector2(12f, 16f), new Vector2(30f, 11f), new Vector2(58f, 11f));
                    p.LineTo(new Vector2(w - 58f, 11f));
                    p.BezierCurveTo(new Vector2(w - 30f, 11f), new Vector2(w - 12f, 16f), new Vector2(w - 12f, 32f));
                    p.LineTo(new Vector2(w - 12f, h - 12f));
                    p.ClosePath();
                });

                foreach (float sx in new[] { 32f, w - 50f })
                {
                    Fill(p, trimDark, () => RoundRect(p, sx, 4f, 18f, h - 4f, 3f, 1f));
                    Fill(p, trim, () => RoundRect(p, sx + 3f, 6f, 4f, h - 8f, 1f, 1f));
                }
                Fill(p, trim, () => RoundRect(p, 0f, h - 14f, w, 14f, 2f, 2f));
                Fill(p, trim, () => RoundRect(p, w * 0.5f - 16f, h - 26f, 32f, 26f, 6f, 2f));

                // A highlight along the top of the dome.
                p.strokeColor = new Color(1f, 1f, 1f, 0.2f);
                p.lineWidth = 3f;
                p.lineCap = LineCap.Round;
                p.BeginPath();
                p.MoveTo(new Vector2(7f, 28f));
                p.BezierCurveTo(new Vector2(7f, 12f), new Vector2(27f, 6f), new Vector2(56f, 6f));
                p.LineTo(new Vector2(w - 56f, 6f));
                p.Stroke();
            }

            private static void Fill(Painter2D p, Color c, Action path)
            {
                p.fillColor = c;
                path();
                p.Fill();
            }

            /// <summary>A rectangle path with one radius for the top corners and one for the bottom.</summary>
            private static void RoundRect(Painter2D p, float x, float y, float w, float h, float rt, float rb)
            {
                rt = Mathf.Clamp(rt, 0.5f, Mathf.Min(w, h) * 0.5f);
                rb = Mathf.Clamp(rb, 0.5f, Mathf.Min(w, h) * 0.5f);
                p.BeginPath();
                p.MoveTo(new Vector2(x + rt, y));
                p.ArcTo(new Vector2(x + w, y), new Vector2(x + w, y + h), rt);
                p.ArcTo(new Vector2(x + w, y + h), new Vector2(x, y + h), rb);
                p.ArcTo(new Vector2(x, y + h), new Vector2(x, y), rb);
                p.ArcTo(new Vector2(x, y), new Vector2(x + w, y), rt);
                p.ClosePath();
            }

            private static Color Shade(Color c, float k)
            {
                Color s = Color.Lerp(ArcadeTheme.BgDeep, c, k);
                s.a = 1f;
                return s;
            }
        }
    }
}
