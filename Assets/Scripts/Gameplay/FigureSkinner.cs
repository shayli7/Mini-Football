using System.Collections.Generic;
using TableFootball.Net;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Dresses all twenty-two player figures in the equipped figure skin.
    ///
    /// Figures are found through <see cref="TableReferences"/> rather than by scanning the scene: it
    /// already owns both teams' rods, and a figure is a <c>Fig*</c> child of a rod — the same
    /// traversal <see cref="RodController"/> uses to name its own role.
    ///
    /// The GOALKEEPER is identified by his rod carrying exactly one figure, which is what makes a
    /// keeper a keeper on any foosball table. Derived rather than taken from the rod list's ordering
    /// (documented as goalie-first) because that ordering is re-established by an editor command and
    /// would silently put the knight on an attacker if it were ever run differently.
    ///
    /// Like the ball and field skinners this is input-agnostic — offline it reads the local
    /// <see cref="Inventory"/>, online the host pushes a choice in through <see cref="SetOverride"/>.
    /// Purely visual: nothing here touches a collider or a rod.
    /// </summary>
    [DisallowMultipleComponent]
    public class FigureSkinner : MonoBehaviour
    {
        /// <summary>One figure and everything needed to re-dress it: its renderer, its team, whether
        /// it is the keeper, and the materials it arrived with (which differ per team).</summary>
        private struct Figure
        {
            public Renderer Renderer;
            public Team Team;
            public bool Keeper;
            public Material[] Originals;
        }

        private readonly List<Figure> figures = new();
        private TableReferences table;
        private bool collected;

        /// <summary>
        /// A skin per team, set from outside. Online this is how each player's OWN figures wear that
        /// player's OWN choice: the host publishes its skin for its side and the guest's for theirs,
        /// so both machines show the same two teams dressed differently.
        ///
        /// Empty offline, where the local player's inventory dresses their own side (see
        /// <see cref="LocalTeam"/>) and the opposition keeps the model's default red or blue.
        /// </summary>
        private readonly Dictionary<Team, string> teamSkins = new();

        /// <summary>
        /// Which side belongs to the player sitting here, so their skin goes on their own figures.
        ///
        /// Red by default because that is the host's side and the side a single player takes in the
        /// menu, before any match has decided otherwise. <c>GameFlow</c> corrects it at kick-off.
        /// </summary>
        private Team localTeam = Team.Red;

        private void Awake()
        {
            // Found rather than passed, like GameFlow's stage camera: the skinner is optional
            // decoration and must never be the reason a table fails to build.
            table = GetComponent<TableReferences>();
            if (table == null) table = FindAnyObjectByType<TableReferences>();
        }

        private void OnEnable()
        {
            Inventory.OnChanged -= Apply;
            Inventory.OnChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            Inventory.OnChanged -= Apply;
        }

        /// <summary>
        /// Dresses ONE team in a specific skin, for an online match where each player brings their
        /// own. An empty id means that side falls back to the rule below.
        /// </summary>
        public void SetTeamSkin(Team team, string id)
        {
            if (string.IsNullOrEmpty(id)) teamSkins.Remove(team);
            else teamSkins[team] = id;
            Apply();
        }

        /// <summary>Forgets both sides' explicit skins. The end of an online match.</summary>
        public void ClearOverride()
        {
            if (teamSkins.Count == 0) return;
            teamSkins.Clear();
            Apply();
        }

        /// <summary>Tells the skinner which side the player here is playing, so their own skin lands
        /// on their own figures. Called by <c>GameFlow</c> at kick-off.</summary>
        public void SetLocalTeam(Team team)
        {
            if (localTeam == team) return;
            localTeam = team;
            Apply();
        }

        /// <summary>
        /// What a given side is wearing: an explicitly set skin first, then — for the player's OWN
        /// side only — whatever they have equipped. The opposing side deliberately falls through to
        /// null, which resolves to the model's own red or blue, so the two teams stay tellable apart
        /// even when a skin repaints the shirts.
        /// </summary>
        public string CurrentIdFor(Team team)
        {
            if (teamSkins.TryGetValue(team, out string id) && !string.IsNullOrEmpty(id))
            {
                return id;
            }

            return team == localTeam ? Inventory.EquippedId(CosmeticKind.FigureSkin) : null;
        }

        /// <summary>
        /// Walks the rods once and records every figure with its team, keeper flag and original
        /// materials.
        ///
        /// Done lazily on the first Apply rather than in Awake because the rods are collected into
        /// TableReferences by its own setup, and Awake order between components is not guaranteed.
        /// </summary>
        private void Collect()
        {
            figures.Clear();
            if (table == null)
            {
                return;
            }

            foreach (Team team in new[] { Team.Red, Team.Blue })
            {
                foreach (RodController rod in table.RodsFor(team))
                {
                    if (rod == null) continue;

                    var onThisRod = new List<Renderer>();
                    foreach (Transform child in rod.GetComponentsInChildren<Transform>(true))
                    {
                        if (child == rod.transform) continue;
                        if (!child.name.StartsWith("Fig", System.StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var r = child.GetComponent<Renderer>();
                        if (r != null) onThisRod.Add(r);
                    }

                    // One figure on the rod means this is the goalkeeper's rod.
                    bool keeperRod = onThisRod.Count == 1;

                    foreach (Renderer r in onThisRod)
                    {
                        figures.Add(new Figure
                        {
                            Renderer = r,
                            Team = team,
                            Keeper = keeperRod,
                            Originals = r.sharedMaterials
                        });
                    }
                }
            }

            // Only counted as done if figures were actually found. Marking it collected on an empty
            // sweep — a null TableReferences, or rods not yet populated — would latch the skinner
            // off for the rest of the session with nothing to say why.
            collected = figures.Count > 0;
        }

        private void Apply()
        {
            if (!collected)
            {
                Collect();
            }

            for (int i = 0; i < figures.Count; i++)
            {
                Figure f = figures[i];
                if (f.Renderer == null) continue;

                // Resolved PER TEAM, not once for the table: the two sides can now be wearing
                // different skins, which is the whole point of each player bringing their own.
                Material[] dressed = FigureSkins.Resolve(CurrentIdFor(f.Team), f.Team, f.Keeper,
                                                         f.Originals);
                if (dressed == null) continue;

                // The whole array is assigned back: sharedMaterials returns a COPY, so mutating an
                // element of it changes nothing on the renderer.
                if (!Same(f.Renderer.sharedMaterials, dressed))
                {
                    f.Renderer.sharedMaterials = dressed;
                }
            }
        }

        private static bool Same(Material[] a, Material[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
