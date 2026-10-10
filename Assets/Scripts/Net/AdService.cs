using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
#endif

namespace TableFootball.Net
{
    /// <summary>
    /// Every ad in the game: rewarded video for the store, and an interstitial after every
    /// <see cref="GamesPerInterstitial"/> finished matches. AdMob on Android; anywhere else (the Editor
    /// included) a placeholder that finishes at once and reports the ad as watched, so the store flow
    /// can be tested without a device.
    ///
    /// Every ad call in the game goes through here and nowhere else, the same rule
    /// <see cref="CloudSaveBackend"/> follows for Cloud Save. <see cref="ShopOffers"/> and the store
    /// never know which network is behind it.
    ///
    /// Nothing here ever blocks or throws: an ad that is not ready, fails or is declined reports that
    /// and the game carries on. A phone with no signal must play exactly like one with a connection.
    ///
    /// TEST vs LIVE. Development builds (and the Editor) request Google's published test units, which
    /// always fill and never pay; release builds request the live units below. Never tap a live ad on
    /// your own device, which can get the account banned. The App ID is separate: it lives in
    /// Google Mobile Ads Settings and is baked into the manifest at build time, so it is the real one
    /// for a release build and cannot be switched here.
    ///
    /// AUDIENCE. Nothing here marks the app child-directed. If the Play Console target audience
    /// includes under-13s, set <c>RequestConfiguration.TagForChildDirectedTreatment</c> (and
    /// <c>TagForUnderAgeOfConsent</c>) before <c>MobileAds.Initialize</c> in <see cref="Initialise"/>.
    /// </summary>
    public static class AdService
    {
        /// <summary>An interstitial plays on the way back to the menu after this many finished matches.</summary>
        public const int GamesPerInterstitial = 3;

        private const string GamesKey = "TableFootball.Ads.GamesSinceInterstitial";

        // The real ad units, served only by release builds.
        private const string LiveRewardedUnitId = "ca-app-pub-9480491937603942/8050679080";
        private const string LiveInterstitialUnitId = "ca-app-pub-9480491937603942/5827840421";

        // Google's published test units: always fill, never pay, and are safe to tap.
        private const string TestRewardedUnitId = "ca-app-pub-3940256099942544/5224354917";
        private const string TestInterstitialUnitId = "ca-app-pub-3940256099942544/1033173712";

        // A development build shows TEST ads, a release build shows live ones. Debug.isDebugBuild is
        // true for the Editor and for any build made with "Development Build" ticked, so a device
        // build that has not been through the store can never request, or accidentally tap, a live ad.
        private static string RewardedUnitId => Debug.isDebugBuild ? TestRewardedUnitId : LiveRewardedUnitId;
        private static string InterstitialUnitId => Debug.isDebugBuild ? TestInterstitialUnitId : LiveInterstitialUnitId;

        /// <summary>Matches finished since the last interstitial, kept across app restarts.</summary>
        public static int GamesSinceInterstitial => PlayerPrefs.GetInt(GamesKey, 0);

        /// <summary>
        /// Counts one finished match toward the next interstitial. Call it once per match that ran to a
        /// result; a match walked out of never gets here.
        /// </summary>
        public static void RecordMatchFinished()
        {
            PlayerPrefs.SetInt(GamesKey, GamesSinceInterstitial + 1);
            PlayerPrefs.Save();
        }

#if UNITY_ANDROID && !UNITY_EDITOR

        private static RewardedAd rewarded;
        private static InterstitialAd interstitial;
        private static bool initialising;
        private static bool initialised;
        private static bool sdkReady;
        private static Action<bool> pendingRewarded;
        private static bool loadingRewarded;
        private static bool loadingInterstitial;

        /// <summary>
        /// Boots the SDK once the first scene is up: asks for consent where the player's region needs
        /// it, and only then initialises AdMob and preloads one ad of each kind. Runs itself, so no
        /// scene object has to remember to start it.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialise()
        {
            if (initialising || initialised)
            {
                return;
            }

            initialising = true;

            // Callbacks on Unity's main thread, so they can touch PlayerPrefs and Unity objects.
            MobileAds.RaiseAdEventsOnUnityMainThread = true;

            try
            {
                ConsentInformation.Update(new ConsentRequestParameters(), updateError =>
                {
                    if (updateError != null)
                    {
                        Debug.LogWarning("Ads: consent update failed — " + updateError.Message);
                    }

                    // Shows the form only if this player's region requires one and it has not been answered.
                    ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
                    {
                        if (formError != null)
                        {
                            Debug.LogWarning("Ads: consent form failed — " + formError.Message);
                        }

                        StartSdk();
                    });
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning("Ads: consent step threw — " + e.Message);
                StartSdk();
            }
        }

        private static void StartSdk()
        {
            if (initialised)
            {
                return;
            }

            if (!ConsentInformation.CanRequestAds())
            {
                // Left startable, so the next ad request tries consent again rather than staying dead
                // for the whole session.
                Debug.LogWarning("Ads: consent does not allow ad requests yet.");
                initialising = false;
                ResolvePending();
                return;
            }

            initialised = true;
            MobileAds.Initialize(_ =>
            {
                sdkReady = true;
                initialising = false;
                LoadRewarded();
                LoadInterstitial();
            });
        }

        private static void LoadRewarded()
        {
            if (!sdkReady || loadingRewarded || rewarded != null)
            {
                return;
            }

            loadingRewarded = true;
            RewardedAd.Load(RewardedUnitId, new AdRequest(), (ad, error) =>
            {
                loadingRewarded = false;
                if (error != null || ad == null)
                {
                    Debug.LogWarning("Ads: rewarded failed to load — " + (error != null ? error.GetMessage() : "no ad"));
                }
                else
                {
                    rewarded = ad;
                }

                ResolvePending();
            });
        }

        /// <summary>Answers a tap that arrived while the ad was still loading: plays it now, or declines.</summary>
        private static void ResolvePending()
        {
            Action<bool> waiting = pendingRewarded;
            pendingRewarded = null;
            if (waiting == null)
            {
                return;
            }

            RewardedAd ad = rewarded;
            if (ad != null && ad.CanShowAd())
            {
                rewarded = null;
                PlayRewarded(ad, waiting);
            }
            else
            {
                waiting(false);
            }
        }

        private static void LoadInterstitial()
        {
            if (!sdkReady || loadingInterstitial || interstitial != null)
            {
                return;
            }

            loadingInterstitial = true;
            InterstitialAd.Load(InterstitialUnitId, new AdRequest(), (ad, error) =>
            {
                loadingInterstitial = false;
                if (error != null || ad == null)
                {
                    Debug.LogWarning("Ads: interstitial failed to load — " + (error != null ? error.GetMessage() : "no ad"));
                    return;
                }

                interstitial = ad;
            });
        }

        /// <summary>
        /// Shows a rewarded ad and calls <paramref name="onDone"/> with true only if the player watched
        /// it to the end. A skipped, failed or unavailable ad reports false, and the caller pays nothing.
        /// </summary>
        public static void ShowRewarded(Action<bool> onDone)
        {
            RewardedAd ready = rewarded;
            if (ready != null && ready.CanShowAd())
            {
                // Taken out before it plays: an ad can be shown once, and the next one loads behind it.
                rewarded = null;
                PlayRewarded(ready, onDone);
                return;
            }

            rewarded = null;

            // A second tap while one is already waiting just declines.
            if (pendingRewarded != null)
            {
                onDone?.Invoke(false);
                return;
            }

            // The SDK never started (no signal at launch, consent not settled): try again now.
            if (!initialised && !initialising)
            {
                Initialise();
            }

            LoadRewarded();

            // Still starting up or loading: wait for the answer instead of declining a tap that was
            // only early. It resolves from the load callback, which fires on success or failure.
            if (initialising || loadingRewarded)
            {
                pendingRewarded = onDone;
                return;
            }

            onDone?.Invoke(false);
        }

        private static void PlayRewarded(RewardedAd ad, Action<bool> onDone)
        {
            bool earned = false;
            bool finished = false;

            void Finish()
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                AudioListener.pause = false;
                ad.Destroy();
                LoadRewarded();
                onDone?.Invoke(earned);
            }

            ad.OnAdFullScreenContentClosed += Finish;
            ad.OnAdFullScreenContentFailed += _ => Finish();

            AudioListener.pause = true;
            ad.Show(_ => earned = true);
        }

        /// <summary>
        /// If <see cref="GamesPerInterstitial"/> matches have finished since the last one, plays an
        /// interstitial and then calls <paramref name="onDone"/>; otherwise calls it straight away.
        /// <paramref name="onDone"/> always runs — an ad that is not ready or fails to show never keeps
        /// the player from the menu, it just waits another match.
        /// </summary>
        public static void ShowInterstitialIfDue(Action onDone)
        {
            if (GamesSinceInterstitial < GamesPerInterstitial)
            {
                onDone?.Invoke();
                return;
            }

            InterstitialAd ad = interstitial;
            if (ad == null || !ad.CanShowAd())
            {
                interstitial = null;
                LoadInterstitial();
                onDone?.Invoke();
                return;
            }

            interstitial = null;

            // Reset on show, not on close: a player who force-quits mid-ad must not be shown another
            // the moment they are back.
            PlayerPrefs.SetInt(GamesKey, 0);
            PlayerPrefs.Save();

            bool finished = false;

            void Finish()
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                AudioListener.pause = false;
                ad.Destroy();
                LoadInterstitial();
                onDone?.Invoke();
            }

            ad.OnAdFullScreenContentClosed += Finish;
            ad.OnAdFullScreenContentFailed += _ => Finish();

            AudioListener.pause = true;
            ad.Show();
        }

#else

        /// <summary>Placeholder: no ad SDK on this platform, so every "ad" finishes at once, watched to the end.</summary>
        public static void ShowRewarded(Action<bool> onDone)
        {
            onDone?.Invoke(true);
        }

        /// <summary>Placeholder: counts down like the real one but shows nothing.</summary>
        public static void ShowInterstitialIfDue(Action onDone)
        {
            if (GamesSinceInterstitial >= GamesPerInterstitial)
            {
                PlayerPrefs.SetInt(GamesKey, 0);
                PlayerPrefs.Save();
                Debug.Log("Ads: (placeholder) interstitial would play here.");
            }

            onDone?.Invoke();
        }

#endif
    }
}
