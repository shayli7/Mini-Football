using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Gives each kind of surface its own feel, which is what makes the ball controllable.
    ///
    /// A single shared material cannot do this. If the ball always takes the bounciest and most
    /// slippery result of a contact, then every touch from a player figure flings it away and
    /// nothing can ever trap or drag it. Foosball needs the two surfaces to behave differently:
    ///
    ///   Figures - low bounce, high grip. The ball deadens against a player so you can hold it,
    ///             nudge it along your line, and pass between your own men.
    ///   Rails   - high bounce, low grip. The ball pings off the cushions and stays lively.
    ///   Pitch   - no bounce, moderate grip, so it rolls rather than skids or hops.
    ///
    /// HOW THE VALUES BELOW ACTUALLY REACH THE BALL — this is the part that is easy to get wrong,
    /// and getting it wrong silently disables this whole class.
    ///
    /// A contact has two materials, each with its own combine mode, and PhysX resolves the conflict
    /// by PRIORITY, not by averaging the intent:
    ///
    ///     Average (0)  <  Minimum (1)  <  Multiply (2)  <  Maximum (3)
    ///
    /// The mode with the higher priority decides how BOTH values are combined. So if the ball's
    /// material asks for Maximum bounce, it outranks anything here set to Average, and every
    /// surface below silently becomes decoration: a figure set to bounce 0.05 still returns
    /// max(ballBounce, 0.05), which is the ball's own number, and the ball pings off a player it
    /// was supposed to deaden against.
    ///
    /// The fix is to invert the priority. The ball carries a neutral Average baseline (the lowest
    /// priority, so it never wins), and each surface here declares the mode that gives it its
    /// character:
    ///
    ///   Figures - Minimum bounce (deadens), Maximum friction (grips)  -> ball control
    ///   Rails   - Maximum bounce (lively),  Minimum friction (slides) -> clean angled rebounds
    ///   Pitch   - Minimum bounce (no hop),  Maximum friction (rolls)
    ///
    /// One rule follows from that, and breaking it is the only way to break this again: the ball's
    /// baseline must sit BETWEEN the Minimum-mode targets and the Maximum-mode targets here. A
    /// Minimum-mode surface can only pull a value DOWN from the ball's baseline, and a Maximum-mode
    /// surface can only push it UP. Ball bounce 0.5 works because 0.15 and 0.02 are below it and
    /// 0.85 is above; ball friction 0.05 works because 0.02 is below it and 0.60 and 0.12 are
    /// above. Raise the ball's bounce past the rail value and the rail stops being lively; lower
    /// the ball's friction past the rail value and the rail stops being slippery.
    ///
    /// Materials are built and assigned at runtime, so there are no material assets to wire up and
    /// every value stays in version control. Put this on the table root; it runs after the
    /// colliders exist.
    /// </summary>
    [DisallowMultipleComponent]
    public class TableSurfaces : MonoBehaviour
    {
        [Header("Player figures — where ball control comes from")]
        [Tooltip("Keep BELOW the ball's own bounciness or it has no effect. High values are what " +
                 "make every touch fire the ball across the table. Power comes from the strike " +
                 "boost on a swung rod, not from bouncy players.")]
        [Range(0f, 1f)]
        [SerializeField] private float figureBounciness = 0.15f;
        [Tooltip("Keep ABOVE the ball's own friction or it has no effect. This is the grip that " +
                 "lets you drag and trap the ball.")]
        [Range(0f, 1f)]
        [SerializeField] private float figureFriction = 0.6f;

        [Header("Rails — where liveliness comes from")]
        [Tooltip("Keep ABOVE the ball's own bounciness or it has no effect.")]
        [Range(0f, 1f)]
        [SerializeField] private float railBounciness = 0.85f;
        [Tooltip("Keep BELOW the ball's own friction or it has no effect. Low friction is what " +
                 "preserves the sideways part of an angled hit, so the ball leaves at the angle " +
                 "it arrived instead of dragging along the rail.")]
        [Range(0f, 1f)]
        [SerializeField] private float railFriction = 0.02f;

        [Header("Pitch")]
        [Tooltip("Keep BELOW the ball's own bounciness or it has no effect. Near zero: the ball " +
                 "should roll, never hop.")]
        [Range(0f, 1f)]
        [SerializeField] private float pitchBounciness = 0.02f;
        [Tooltip("Keep ABOVE the ball's own friction or it has no effect. This is the ball's main " +
                 "brake now that air drag is almost off — raise it if the ball feels skittish, " +
                 "lower it if play still feels sluggish.")]
        [Range(0f, 1f)]
        [SerializeField] private float pitchFriction = 0.12f;

        [Header("Object names")]
        [SerializeField] private string figurePrefix = "Fig";
        [SerializeField] private string fieldName = "Field";
        [SerializeField] private string[] railNames = { "Rail_Lp", "Rail_Lm", "Rail_Sp", "Rail_Sm", "GoalBack" };

        private void Awake()
        {
            Apply();
        }

        [ContextMenu("Apply surface materials")]
        public void Apply()
        {
            // Each surface declares the combine mode that makes it authoritative for the property
            // that defines it — see the class comment for why the mode matters more than the value.
            PhysicsMaterial figures = Build("Figure", figureBounciness, PhysicsMaterialCombine.Minimum,
                                            figureFriction, PhysicsMaterialCombine.Maximum);
            PhysicsMaterial rails = Build("Rail", railBounciness, PhysicsMaterialCombine.Maximum,
                                          railFriction, PhysicsMaterialCombine.Minimum);
            PhysicsMaterial pitch = Build("Pitch", pitchBounciness, PhysicsMaterialCombine.Minimum,
                                          pitchFriction, PhysicsMaterialCombine.Maximum);

            int figureCount = 0, railCount = 0, pitchCount = 0;

            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            {
                string colliderName = collider.name;

                if (colliderName.StartsWith(figurePrefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    collider.sharedMaterial = figures;
                    figureCount++;
                }
                else if (colliderName.Equals(fieldName, System.StringComparison.OrdinalIgnoreCase))
                {
                    collider.sharedMaterial = pitch;
                    pitchCount++;
                }
                else if (MatchesRail(colliderName))
                {
                    collider.sharedMaterial = rails;
                    railCount++;
                }
            }

            Debug.Log($"{name}: surfaces applied — {figureCount} figures, {railCount} rails, " +
                      $"{pitchCount} pitch.", this);

            WarnIfBallOutranksSurfaces();
        }

        /// <summary>
        /// Checks the values here can actually reach the ball, and names the ones that cannot.
        ///
        /// Worth the code: when this rule is broken nothing errors, nothing looks wrong, and the
        /// surface simply has no effect — which is how the figures spent this project flinging the
        /// ball away while an inspector field labelled "grip" sat at 0.7 doing nothing.
        /// </summary>
        private void WarnIfBallOutranksSurfaces()
        {
            BallController ball = FindAnyObjectByType<BallController>();
            if (ball == null)
            {
                return;
            }

            float b = ball.BaselineBounciness;
            float f = ball.BaselineFriction;

            var problems = new System.Text.StringBuilder();

            // Minimum-mode values can only pull DOWN from the ball's baseline; Maximum-mode values
            // can only push it UP. Anything on the wrong side of the baseline is inert.
            if (figureBounciness > b) problems.AppendLine($"  figure bounce {figureBounciness:0.##} > ball {b:0.##} — figures will bounce at {b:0.##}, not {figureBounciness:0.##}");
            if (pitchBounciness > b) problems.AppendLine($"  pitch bounce {pitchBounciness:0.##} > ball {b:0.##} — the ball will hop");
            if (railBounciness < b) problems.AppendLine($"  rail bounce {railBounciness:0.##} < ball {b:0.##} — rails will not be lively");
            if (figureFriction < f) problems.AppendLine($"  figure friction {figureFriction:0.##} < ball {f:0.##} — no grip, so no ball control");
            if (pitchFriction < f) problems.AppendLine($"  pitch friction {pitchFriction:0.##} < ball {f:0.##} — the pitch will not slow the ball");
            if (railFriction > f) problems.AppendLine($"  rail friction {railFriction:0.##} > ball {f:0.##} — the ball will drag along rails instead of rebounding");

            if (problems.Length > 0)
            {
                Debug.LogWarning($"{name}: these surface values are outranked by the ball's own " +
                                 $"material and will have NO effect:\n{problems}" +
                                 "Fix by moving the value past the ball's baseline, or by changing " +
                                 "the ball's baseline on BallController.", this);
            }
        }

        private bool MatchesRail(string colliderName)
        {
            foreach (string rail in railNames)
            {
                // StartsWith catches the ".001" duplicates the model uses.
                if (colliderName.StartsWith(rail, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static PhysicsMaterial Build(string label,
                                             float bounciness, PhysicsMaterialCombine bounceCombine,
                                             float friction, PhysicsMaterialCombine frictionCombine)
        {
            return new PhysicsMaterial($"{label} (runtime)")
            {
                bounciness = bounciness,
                dynamicFriction = friction,
                staticFriction = friction,
                bounceCombine = bounceCombine,
                frictionCombine = frictionCombine
            };
        }
    }
}
