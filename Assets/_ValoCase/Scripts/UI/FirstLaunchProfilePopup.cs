using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Profile;
using ValoCase.Services.Backend;

namespace ValoCase.UI
{
    /// <summary>
    /// One-time first-launch profile setup: new players are offered a nickname, a country
    /// and an avatar before playing. Shown by GameContext once the session can persist the
    /// choice (after backend boot sync, or immediately in local editor mode). Built at
    /// runtime with zero prefab dependency, same pattern as NoInternetPopup.
    /// Completion is stored in SaveDataRoot.profileSetupCompleted.
    ///
    /// Offered, not demanded: CONFIRM works with the panel untouched. The backend names an
    /// unnamed account AgentXXXX, stores a missing country as NULL, and has always given
    /// new accounts a default avatar, so the only thing this screen can usefully refuse is
    /// a nickname that was typed and breaks a rule — see <see cref="ProfileSetupGate"/>.
    ///
    /// The three choices are kept in <see cref="ProfileSetupDraft"/> as they are made, so
    /// a failed registration or an app closed mid-setup does not throw them away.
    /// </summary>
    public sealed class FirstLaunchProfilePopup : MonoBehaviour
    {
        static FirstLaunchProfilePopup _instance;

        static readonly Color Backdrop   = new Color(0f, 0f, 0f, 0.85f);
        static readonly Color CardBg     = new Color(0.051f, 0.067f, 0.090f, 1f);
        static readonly Color InputBg    = new Color(0.086f, 0.106f, 0.137f, 1f);
        static readonly Color CellBg     = new Color(0.086f, 0.106f, 0.137f, 1f);
        static readonly Color Accent     = new Color(1f, 0.275f, 0.333f, 1f);
        static readonly Color AccentDim  = new Color(1f, 0.275f, 0.333f, 0.28f);
        // Muted accent for the confirm button while the typed nickname breaks a rule.
        // Was a neutral grey — which reads as DISABLED, and real installs showed players
        // sitting on this panel without ever tapping the button that would have told
        // them what was wrong. Dimmed-but-warm keeps it clearly tappable.
        static readonly Color ConfirmIdle = new Color(0.478f, 0.169f, 0.196f, 1f);
        // Label tone on the dimmed button: readable, warm, clearly not the ready state.
        static readonly Color ConfirmIdleText = new Color(0.949f, 0.812f, 0.831f, 1f);
        // Warning line under the input. Deliberately a brighter red than the brand accent
        // so it reads as an error rather than decoration.
        static readonly Color ErrorRed   = new Color(1f, 0.325f, 0.325f, 1f);
        static readonly Color TextMain   = new Color(0.961f, 0.961f, 0.961f, 1f);
        static readonly Color TextDim    = new Color(0.541f, 0.569f, 0.651f, 1f);
        static readonly Color DarkText   = new Color(0.043f, 0.055f, 0.082f, 1f);

        TMP_InputField  _nameInput;
        TextMeshProUGUI _counterLbl;
        TextMeshProUGUI _errorLbl;
        Button          _confirmBtn;
        Image           _confirmImg;
        TextMeshProUGUI _confirmLbl;
        Transform       _grid;
        Button          _countryBtn;
        Outline         _countryOutline;
        TextMeshProUGUI _countryLbl;
        // Starts on AA — "asked, chose not to say" — so the panel opens with a real,
        // already-valid answer rather than a blank the player has to fill in. Tapping the
        // row swaps it for a country; leaving it alone sends AA.
        string          _countryCode = CountryCatalog.NoCountryCode;
        string          _selectedKey;
        Sprite          _selectedSprite;
        bool            _saving;
        // Taps on the dim area outside the card. The second one confirms — see
        // OnBackdropTapped. Deliberately never reset: two stray taps minutes apart
        // still mean the player is done with this panel, and everything on it is
        // optional anyway.
        int             _backdropTaps;

        // ── Entry point ───────────────────────────────────────────────────────

        public static void TryShow()
        {
            if (_instance != null) return;
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Save?.Data == null) return;
            if (IsSetupComplete(ctx)) return;

            var go = new GameObject("FirstLaunchProfilePopup");
            _instance = go.AddComponent<FirstLaunchProfilePopup>();
        }

        static bool IsSetupComplete(GameContext ctx)
        {
            var data = ctx.Save.Data;
            if (data.profileSetupCompleted) return true;

            // Existing users who already customized name or avatar are never re-prompted;
            // backfill the flag so this legacy check runs only once.
            bool legacyProfile = PlayerProfileData.HasSavedAvatarSelection ||
                (!string.IsNullOrWhiteSpace(data.playerName) && data.playerName != "Agent");
            if (legacyProfile)
            {
                data.profileSetupCompleted = true;
                ctx.Save.Save();
                return true;
            }
            return false;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // A setup that never finished usually ends by the app being backgrounded and then
        // killed, where OnApplicationQuit does not run. Writing the draft to disk here is
        // what makes the choices survive that.
        void OnApplicationPause(bool paused)
        {
            if (paused) ProfileSetupDraft.Flush();
        }

        void OnApplicationQuit() => ProfileSetupDraft.Flush();

        IEnumerator Start()
        {
            // Wait a frame so scene UI (canvas, screens) finished building first.
            yield return null;

            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Save?.Data == null || IsSetupComplete(ctx))
            {
                Destroy(gameObject);
                yield break;
            }

            // Skipping the panel used to let the player into the session with no
            // account, no nickname and no country — silently, and permanently, since
            // the next launch takes the same branch. Setup gates progress, so a scene
            // with no canvas gets one built for it rather than the gate being dropped.
            var parent = FindPopupParent();
            if (parent == null)
            {
                Debug.LogWarning("[FirstLaunchProfile] No Canvas found — building a fallback overlay canvas.");
                parent = UIBuild.CreateFallbackOverlayCanvas("FirstLaunchProfileCanvas");
            }

            ProfileManager.EnsureInitialized();
            transform.SetParent(parent, false);
            BuildUi();
            RestoreDraft();
            transform.SetAsLastSibling();
            OnboardingTelemetry.Emit(OnboardingTelemetry.NicknameScreenShown);
            Debug.Log("[FirstLaunchProfile] Popup shown (profile setup pending).");
        }

        static Transform FindPopupParent()
        {
            var safeArea = GameObject.Find("SafeArea");
            if (safeArea != null) return safeArea.transform;

            Canvas best = null;
            int bestOrder = int.MinValue;
            foreach (var c in FindObjectsOfType<Canvas>())
                if (c.isRootCanvas && c.sortingOrder > bestOrder) { bestOrder = c.sortingOrder; best = c; }
            return best != null ? best.transform : null;
        }

        // ── Confirm flow ──────────────────────────────────────────────────────

        // Two taps outside the card behave exactly like pressing CONFIRM. One tap is
        // never enough on purpose — dismissing the keyboard taps this same area, and a
        // single stray touch must not submit the form. The confirm path's own
        // validation still runs, so a half-typed invalid name shows its error instead
        // of being sent.
        void OnBackdropTapped()
        {
            if (_saving) return;
            _backdropTaps++;
            if (_backdropTaps >= 2) OnConfirmClicked();
        }

        void OnConfirmClicked()
        {
            if (_saving) return;
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Save?.Data == null) return;

            // The name is validated, normalised, and only then sent. The value that goes
            // to the server is the canonical one the validator produced, not the raw
            // field text, so what the backend stores is what was already checked here.
            //
            // An empty field is the one refusal that no longer stops anything: it means
            // the player did not want to pick a name, and the server answers that with an
            // AgentXXXX of its own. Every other reason is still a refusal, because the
            // server would answer those with a 400.
            var reason = NicknameValidator.Classify(_nameInput != null ? _nameInput.text : null);
            if (reason != NicknameRejectionReason.None && reason != NicknameRejectionReason.Blank)
            {
                ShowError(NicknameMessages.For(reason));
                OnboardingTelemetry.Emit(OnboardingTelemetry.NicknameRejected,
                    rejectionReason: NicknameValidator.WireName(reason));
                return;
            }

            // Empty when the field was blank. That is what the backend wants for "assign
            // one for me" — not a name this client made up.
            var nickname = reason == NicknameRejectionReason.None
                ? NicknameValidator.Normalize(_nameInput.text)
                : string.Empty;

            // Never empty: "AA" when nothing was picked. A registration that stated no
            // country left the save holding whatever an earlier account had put there,
            // which is how a player who touched nothing ended up with a country.
            var country = CountryCatalog.ForWire(_countryCode);

            ShowError(null);
            OnboardingTelemetry.Emit(OnboardingTelemetry.NicknameConfirmClicked);
            SetSaving(true);

            if (ctx.BackendEnabled)
            {
                // A first-time player has no account yet — boot deliberately skipped
                // registration so that merely opening the app creates nothing. Create it
                // now, then name it. Returning players already have a token and go
                // straight to the rename.
                ctx.RegisterGuestBackend(nickname, country,
                    nameApplied =>
                    {
                        // The account is created with the nickname already set, so the
                        // rename call only runs against a backend that ignored it.
                        if (nameApplied) { SaveAvatarThenFinish(ctx); return; }

                        // Nothing to rename to. We asked for no name, so whatever the
                        // account is called is the server's to decide — sending the empty
                        // field on would only earn a refusal from the same validator that
                        // let it through a moment ago.
                        if (nickname.Length == 0) { SaveAvatarThenFinish(ctx); return; }

                        ctx.SaveDisplayNameBackend(nickname,
                            _ => SaveAvatarThenFinish(ctx),
                            err =>
                            {
                                ShowError(string.IsNullOrEmpty(err) ? "Could not save. Please try again." : err);
                                SetSaving(false);
                            });
                    },
                    err =>
                    {
                        ShowError(string.IsNullOrEmpty(err) ? "Could not connect. Please try again." : err);
                        SetSaving(false);
                    });
                return;
            }

            // Local editor session: there is no server to assign the missing pieces, so a
            // blank choice simply leaves the existing default alone rather than writing an
            // empty name over it.
            if (nickname.Length > 0)
            {
                PlayerProfileData.SetUsername(nickname);
                ctx.Save.Data.playerName = nickname;
            }
            if (_selectedSprite != null) PlayerProfileData.SetAvatar(_selectedSprite, _selectedKey);
            ctx.Save.Data.countryCode = country;
            MarkCompleteAndClose(ctx);
        }

        void SaveAvatarThenFinish(GameContext ctx)
        {
            if (string.IsNullOrEmpty(_selectedKey) || _selectedSprite == null)
            {
                MarkCompleteAndClose(ctx);
                return;
            }

            ctx.SaveAvatarBackend(_selectedKey,
                _ =>
                {
                    PlayerProfileData.SetAvatar(_selectedSprite, _selectedKey);
                    MarkCompleteAndClose(ctx);
                },
                err =>
                {
                    ShowError(string.IsNullOrEmpty(err) ? "Could not save avatar. Please try again." : err);
                    SetSaving(false);
                });
        }

        void MarkCompleteAndClose(GameContext ctx)
        {
            ctx.Save.Data.profileSetupCompleted = true;
            ctx.Save.Save();
            // The choices now live on the account and in the save; the draft has nothing
            // left to protect.
            ProfileSetupDraft.Clear();
            // The country is stored after the name, so the top bar — which renders the two
            // together — has to be told once more before the panel closes.
            PlayerProfileData.NotifyProfileChanged();
            Debug.Log($"[FirstLaunchProfile] Setup complete — name={PlayerProfileData.Username} " +
                      $"avatar={_selectedKey} country={ctx.Save.Data.countryCode}");
            Destroy(gameObject);
        }

        void ShowError(string message)
        {
            if (_errorLbl != null) _errorLbl.text = message ?? string.Empty;
        }

        void SetSaving(bool saving)
        {
            _saving = saving;
            if (_confirmLbl != null) _confirmLbl.text = saving ? "SAVING..." : "CONFIRM";
            if (_nameInput  != null) _nameInput.interactable = !saving;
            if (_countryBtn != null) _countryBtn.interactable = !saving;
            RefreshConfirmState();
        }

        // Grey while the typed nickname breaks a rule, so the player can see there is a
        // problem before pressing anything. An untouched panel is ready — nothing on it is
        // required. It remains clickable while grey: pressing it is how they get told what
        // is wrong, and it is the only moment at which a refused nickname reaches the funnel.
        void RefreshConfirmState()
        {
            bool ready = !_saving &&
                         ProfileSetupGate.IsReady(_nameInput != null ? _nameInput.text : null);

            if (_confirmBtn != null) _confirmBtn.interactable = !_saving;
            if (_confirmImg != null) _confirmImg.color = ready ? Accent : ConfirmIdle;
            if (_confirmLbl != null && !_saving)
                _confirmLbl.color = ready ? DarkText : ConfirmIdleText;
        }

        // Runs on every keystroke: the complaint appears WHILE the rule is being broken,
        // not only after a tap on the confirm button. Players do not tap a button that
        // looks off — the panel has to speak first.
        void OnNicknameChanged(string value)
        {
            ShowLiveValidation(value);
            UpdateCounter(value);
            ProfileSetupDraft.SetNickname(value);
            RefreshConfirmState();
        }

        // Everything except TooShort is worth interrupting the typing for. Someone two
        // keys into a name does not need "at least 3 characters" yet — that message
        // still arrives via the confirm tap if they genuinely stop short. Valid and
        // blank both clear the line, so it never lingers under a field that is fine.
        void ShowLiveValidation(string value)
        {
            var reason = NicknameValidator.Classify(value);
            bool quiet = reason == NicknameRejectionReason.None ||
                         reason == NicknameRejectionReason.Blank ||
                         reason == NicknameRejectionReason.TooShort;
            ShowError(quiet ? null : NicknameMessages.For(reason));
        }

        // "12/15" beside the NICKNAME label. The input field deliberately allows more
        // UTF-16 units than 15 visible characters (grapheme clusters have no fixed
        // size), so without this the only sign of an over-long name was the dimmed
        // button. Hidden while empty; turns warning-red past the limit.
        void UpdateCounter(string value)
        {
            if (_counterLbl == null) return;
            var normalized = NicknameValidator.Normalize(value);
            int length = NicknameValidator.GraphemeLength(normalized);
            _counterLbl.text  = length == 0 ? string.Empty : $"{length}/{NicknameValidator.MaxLength}";
            _counterLbl.color = length > NicknameValidator.MaxLength ? ErrorRed : TextDim;
        }

        void OnCountryButtonClicked()
        {
            if (_saving) return;
            CountryPickerPopup.Show(transform.parent, _countryCode, OnCountryPicked);
        }

        void OnCountryPicked(string code)
        {
            _countryCode = CountryCatalog.Normalize(code);
            ProfileSetupDraft.SetCountryCode(_countryCode);
            ProfileSetupDraft.Flush();
            ShowError(null);
            UpdateCountryLabel();
            RefreshConfirmState();
        }

        // AA is a selection like any other, so the row always reads as filled in. Only the
        // outline distinguishes the default from a country the player went and picked.
        void UpdateCountryLabel()
        {
            if (_countryLbl == null) return;
            var label = CountryCatalog.LabelFor(_countryCode);
            if (label.Length == 0) label = CountryCatalog.NoCountry.Label;

            _countryLbl.text = label;
            if (_countryOutline != null)
                _countryOutline.effectColor =
                    CountryCatalog.NoCountryCode == _countryCode ? AccentDim : Accent;
        }

        // Puts back whatever the player had chosen before the app closed or registration
        // failed. Nothing here selects for them: an absent draft leaves the panel in its
        // untouched state.
        void RestoreDraft()
        {
            var nickname = ProfileSetupDraft.Nickname;
            if (_nameInput != null && !string.IsNullOrEmpty(nickname)) _nameInput.text = nickname;

            var avatarKey = ProfileSetupDraft.AvatarKey;
            var avatars = ProfileManager.Avatars;
            if (!string.IsNullOrEmpty(avatarKey) && avatars != null)
            {
                foreach (var (name, sprite) in avatars)
                    if (string.Equals(name, avatarKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedKey    = name;
                        _selectedSprite = sprite;
                        break;
                    }
            }

            // An absent draft leaves the default in place rather than blanking it.
            var draftCountry = ProfileSetupDraft.CountryCode;
            if (draftCountry.Length > 0) _countryCode = draftCountry;

            RefreshCellHighlights();
            UpdateCountryLabel();
            RefreshConfirmState();
        }

        // ── UI construction ───────────────────────────────────────────────────

        void BuildUi()
        {
            var rootRt = gameObject.GetComponent<RectTransform>();
            if (rootRt == null) rootRt = gameObject.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;

            var dim = gameObject.AddComponent<Image>();
            dim.color         = Backdrop;
            dim.raycastTarget = true;

            // Same family as the fan notice's tap-anywhere close, one notch more
            // careful because this panel has a text field: the first outside tap is
            // free (it is how the keyboard gets dismissed), the second acts as CONFIRM.
            var dimBtn = gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(OnBackdropTapped);

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Outline));
            card.transform.SetParent(transform, false);
            // Swallows taps on the card body so they never bubble up to the backdrop
            // button: only genuine outside-the-panel taps count toward quick-confirm.
            card.AddComponent<CardTapCatcher>();
            var cardRt = (RectTransform)card.transform;
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(760f, 1140f);
            card.GetComponent<Image>().color = CardBg;
            var cardOl = card.GetComponent<Outline>();
            cardOl.effectColor    = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
            cardOl.effectDistance = new Vector2(2f, -2f);

            var title = UIBuild.MakeTmp(card.transform, "Title", "CHOOSE YOUR AGENT", 36f, FontStyles.Bold, Accent);
            title.alignment        = TextAlignmentOptions.Center;
            title.characterSpacing = 4f;
            TopBand(title.rectTransform, -44f, 48f);

            var subtitle = UIBuild.MakeTmp(card.transform, "Subtitle",
                SubtitleText, 20f, FontStyles.Normal, TextDim);
            subtitle.alignment = TextAlignmentOptions.Center;
            TopBand(subtitle.rectTransform, -98f, 30f);

            var nameHint = UIBuild.MakeTmp(card.transform, "NameHint", "NICKNAME", 18f, FontStyles.Bold, TextDim);
            nameHint.alignment        = TextAlignmentOptions.MidlineLeft;
            nameHint.characterSpacing = 3f;
            TopBand(nameHint.rectTransform, -158f, 26f, 60f);

            // Same band as the NICKNAME label, right edge. Text and color are owned by
            // UpdateCounter; starts empty so the untouched panel shows no numbers.
            _counterLbl = UIBuild.MakeTmp(card.transform, "NameCount", "", 18f, FontStyles.Bold, TextDim);
            _counterLbl.alignment = TextAlignmentOptions.MidlineRight;
            TopBand(_counterLbl.rectTransform, -158f, 26f, 60f);

            BuildNameInput(card.transform);

            var countryHint = UIBuild.MakeTmp(card.transform, "CountryHint", "COUNTRY", 18f, FontStyles.Bold, TextDim);
            countryHint.alignment        = TextAlignmentOptions.MidlineLeft;
            countryHint.characterSpacing = 3f;
            TopBand(countryHint.rectTransform, -294f, 26f, 60f);

            BuildCountryButton(card.transform);

            var avHint = UIBuild.MakeTmp(card.transform, "AvatarHint", "SELECT AVATAR", 18f, FontStyles.Bold, TextDim);
            avHint.alignment        = TextAlignmentOptions.MidlineLeft;
            avHint.characterSpacing = 3f;
            TopBand(avHint.rectTransform, -426f, 26f, 60f);

            BuildAvatarScrollGrid(card.transform);
            PopulateAvatars();

            _errorLbl = UIBuild.MakeTmp(card.transform, "Error", "", 20f, FontStyles.Bold, ErrorRed);
            _errorLbl.alignment          = TextAlignmentOptions.Center;
            _errorLbl.enableWordWrapping = true;
            var eRt = _errorLbl.rectTransform;
            eRt.anchorMin        = new Vector2(0f, 0f);
            eRt.anchorMax        = new Vector2(1f, 0f);
            eRt.pivot            = new Vector2(0.5f, 0f);
            eRt.anchoredPosition = new Vector2(0f, 148f);
            eRt.sizeDelta        = new Vector2(-80f, 56f);

            BuildConfirmButton(card.transform);
        }

        static void TopBand(RectTransform rt, float y, float height, float sidePad = 40f)
        {
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta        = new Vector2(-sidePad * 2f, height);
        }

        void BuildNameInput(Transform parent)
        {
            var wrap = new GameObject("NameInputWrap", typeof(RectTransform), typeof(Image), typeof(Outline));
            wrap.transform.SetParent(parent, false);
            var wrapRt = (RectTransform)wrap.transform;
            TopBand(wrapRt, -192f, 84f, 60f);
            wrap.GetComponent<Image>().color = InputBg;
            var wrapOl = wrap.GetComponent<Outline>();
            wrapOl.effectColor    = AccentDim;
            wrapOl.effectDistance = new Vector2(1.5f, -1.5f);

            var fieldGo = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            fieldGo.transform.SetParent(wrap.transform, false);
            var fieldRt = (RectTransform)fieldGo.transform;
            fieldRt.anchorMin = Vector2.zero; fieldRt.anchorMax = Vector2.one;
            fieldRt.offsetMin = Vector2.zero; fieldRt.offsetMax = Vector2.zero;
            fieldGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var taGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            taGo.transform.SetParent(fieldGo.transform, false);
            var taRt = (RectTransform)taGo.transform;
            taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(24f, 10f); taRt.offsetMax = new Vector2(-24f, -10f);

            // The subtitle already says everything is optional, but the field is where
            // that has to be believed: an inviting blank beats a demand for input.
            var ph = UIBuild.MakeTmp(taGo.transform, "Placeholder", PlaceholderText, 26f, FontStyles.Italic, TextDim);
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            UIBuild.Stretch(ph.rectTransform);

            var txt = UIBuild.MakeTmp(taGo.transform, "Text", "", 26f, FontStyles.Bold, TextMain);
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            UIBuild.Stretch(txt.rectTransform);

            _nameInput = fieldGo.GetComponent<TMP_InputField>();
            _nameInput.textViewport   = taRt;
            _nameInput.textComponent  = txt;
            _nameInput.placeholder    = ph;
            // The field's hard stop is the backend's raw-storage guard, not the 15
            // user-visible characters the rule is about. A grapheme cluster can span
            // several UTF-16 units, so stopping the field at 15 would silently truncate
            // a legitimate name in scripts that use combining marks. Past 15 clusters the
            // validator says so in words instead.
            _nameInput.characterLimit = NicknameValidator.MaxUtf16Length;
            _nameInput.contentType    = TMP_InputField.ContentType.Standard;
            _nameInput.caretColor     = Accent;
            // Starts empty on purpose: a pre-filled name let players confirm without ever
            // choosing one, which is how accounts ended up named "Agent".
            _nameInput.text           = string.Empty;
            _nameInput.onValueChanged.AddListener(OnNicknameChanged);
        }

        // Deliberately a button that opens the picker rather than a dropdown: 249 entries
        // are not browsable without a search box, and the picker is the same one Settings
        // opens.
        void BuildCountryButton(Transform parent)
        {
            var go = new GameObject("CountryBtn",
                typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(parent, false);
            TopBand((RectTransform)go.transform, -324f, 84f, 60f);
            go.GetComponent<Image>().color = InputBg;

            _countryOutline = go.GetComponent<Outline>();
            _countryOutline.effectColor    = AccentDim;
            _countryOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // Text and styling are set by UpdateCountryLabel, which RestoreDraft calls
            // before the panel is visible.
            _countryLbl = UIBuild.MakeTmp(go.transform, "Lbl", CountryCatalog.NoCountry.Label,
                                          26f, FontStyles.Bold, TextMain);
            _countryLbl.alignment = TextAlignmentOptions.MidlineLeft;
            var lRt = _countryLbl.rectTransform;
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
            lRt.offsetMin = new Vector2(24f, 0f); lRt.offsetMax = new Vector2(-64f, 0f);

            var caret = UIBuild.MakeTmp(go.transform, "Caret", "▾", 26f, FontStyles.Bold, Accent);
            caret.alignment = TextAlignmentOptions.Center;
            var cRt = caret.rectTransform;
            cRt.anchorMin = cRt.anchorMax = cRt.pivot = new Vector2(1f, 0.5f);
            cRt.anchoredPosition = new Vector2(-22f, 0f);
            cRt.sizeDelta        = new Vector2(40f, 40f);

            _countryBtn = go.GetComponent<Button>();
            _countryBtn.transition = Selectable.Transition.None;
            UIBuild.WireButtonClick(_countryBtn);
            _countryBtn.onClick.AddListener(OnCountryButtonClicked);
        }

        void BuildAvatarScrollGrid(Transform parent)
        {
            var scrollGo = new GameObject("AvatarScroll",
                typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            TopBand(scrollRt, -456f, 456f, 50f);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal         = false;
            sr.vertical           = true;
            sr.movementType       = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity  = 40f;

            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            sr.viewport = vpRt;

            var gridGo = new GameObject("Content",
                typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            gridGo.transform.SetParent(vpRt, false);
            var gridRt = (RectTransform)gridGo.transform;
            gridRt.anchorMin        = new Vector2(0f, 1f);
            gridRt.anchorMax        = new Vector2(1f, 1f);
            gridRt.pivot            = new Vector2(0.5f, 1f);
            gridRt.anchoredPosition = Vector2.zero;
            gridRt.sizeDelta        = Vector2.zero;
            sr.content              = gridRt;

            var glg = gridGo.GetComponent<GridLayoutGroup>();
            glg.cellSize        = new Vector2(150f, 168f);
            glg.spacing         = new Vector2(14f, 14f);
            glg.padding         = new RectOffset(8, 8, 8, 8);
            glg.childAlignment  = TextAnchor.UpperCenter;
            glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 4;

            gridGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _grid = gridGo.transform;
        }

        void PopulateAvatars()
        {
            var avatars = ProfileManager.Avatars;
            if (avatars == null || avatars.Count == 0)
            {
                var msg = UIBuild.MakeTmp(_grid, "NoAvatars", "No avatars available", 20f, FontStyles.Normal, TextDim);
                msg.alignment = TextAlignmentOptions.Center;
                return;
            }

            _selectedKey    = avatars[0].name;
            _selectedSprite = avatars[0].sprite;

            foreach (var (name, sprite) in avatars)
                BuildAvatarCell(name, sprite);
            RefreshCellHighlights();
        }

        void BuildAvatarCell(string key, Sprite sprite)
        {
            var cell = new GameObject($"AV_{key}", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            cell.transform.SetParent(_grid, false);
            cell.GetComponent<Image>().color = CellBg;

            var maskGo = new GameObject("Mask", typeof(RectTransform), typeof(Image), typeof(Mask));
            maskGo.transform.SetParent(cell.transform, false);
            var maskRt = (RectTransform)maskGo.transform;
            maskRt.anchorMin = maskRt.anchorMax = new Vector2(0.5f, 1f);
            maskRt.pivot            = new Vector2(0.5f, 1f);
            maskRt.anchoredPosition = new Vector2(0f, -12f);
            maskRt.sizeDelta        = new Vector2(112f, 112f);
            var maskImg = maskGo.GetComponent<Image>();
            maskImg.sprite        = UIBuild.CircleSprite();
            maskImg.raycastTarget = false;
            maskGo.GetComponent<Mask>().showMaskGraphic = false;

            var avGo = new GameObject("Av", typeof(RectTransform), typeof(Image));
            avGo.transform.SetParent(maskGo.transform, false);
            var avRt = (RectTransform)avGo.transform;
            avRt.anchorMin = Vector2.zero; avRt.anchorMax = Vector2.one;
            avRt.offsetMin = Vector2.zero; avRt.offsetMax = Vector2.zero;
            var avImg = avGo.GetComponent<Image>();
            avImg.sprite         = sprite;
            avImg.preserveAspect = true;
            avImg.raycastTarget  = false;

            var lbl = UIBuild.MakeTmp(cell.transform, "Name", key, 16f, FontStyles.Bold, TextDim);
            lbl.alignment = TextAlignmentOptions.Center;
            var lRt = lbl.rectTransform;
            lRt.anchorMin        = new Vector2(0f, 0f);
            lRt.anchorMax        = new Vector2(1f, 0f);
            lRt.pivot            = new Vector2(0.5f, 0f);
            lRt.anchoredPosition = new Vector2(0f, 10f);
            lRt.sizeDelta        = new Vector2(0f, 24f);

            var btn = cell.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            UIBuild.WireButtonClick(btn);
            string capKey = key; Sprite capSprite = sprite;
            btn.onClick.AddListener(() =>
            {
                if (_saving) return;
                _selectedKey    = capKey;
                _selectedSprite = capSprite;
                ProfileSetupDraft.SetAvatarKey(capKey);
                ProfileSetupDraft.Flush();
                ShowError(null);
                RefreshCellHighlights();
                RefreshConfirmState();
            });
        }

        void RefreshCellHighlights()
        {
            if (_grid == null) return;
            foreach (Transform cell in _grid)
            {
                bool sel = cell.name == $"AV_{_selectedKey}";
                var ol = cell.GetComponent<Outline>();
                if (ol != null)
                {
                    ol.effectColor    = sel ? Accent : AccentDim;
                    ol.effectDistance = new Vector2(sel ? 3f : 1f, -(sel ? 3f : 1f));
                }
                var lbl = cell.Find("Name")?.GetComponent<TextMeshProUGUI>();
                if (lbl != null) lbl.color = sel ? Accent : TextDim;
            }
        }

        void BuildConfirmButton(Transform parent)
        {
            var btnGo = new GameObject("ConfirmBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);
            var btnRt = (RectTransform)btnGo.transform;
            btnRt.anchorMin        = new Vector2(0.5f, 0f);
            btnRt.anchorMax        = new Vector2(0.5f, 0f);
            btnRt.pivot            = new Vector2(0.5f, 0f);
            btnRt.anchoredPosition = new Vector2(0f, 44f);
            btnRt.sizeDelta        = new Vector2(640f, 92f);

            _confirmImg = btnGo.GetComponent<Image>();
            _confirmImg.color = Accent;

            _confirmBtn = btnGo.GetComponent<Button>();
            _confirmBtn.transition    = Selectable.Transition.None;
            _confirmBtn.targetGraphic = _confirmImg;
            UIBuild.WireButtonClick(_confirmBtn);
            _confirmBtn.onClick.AddListener(OnConfirmClicked);

            _confirmLbl = UIBuild.MakeTmp(btnGo.transform, "Lbl", "CONFIRM", 28f, FontStyles.Bold, DarkText);
            _confirmLbl.alignment        = TextAlignmentOptions.Center;
            _confirmLbl.characterSpacing = 3f;
            UIBuild.Stretch(_confirmLbl.rectTransform);

            // Nothing is chosen yet, so the button starts grey.
            RefreshConfirmState();
        }

        // ── Player-facing text ────────────────────────────────────────────────
        // Device language, matching how NicknameMessages picks its wording.

        static bool IsTurkish => Application.systemLanguage == SystemLanguage.Turkish;

        static string SubtitleText => IsTurkish
            ? "Hepsi isteğe bağlı — sonradan Ayarlar'dan değiştirebilirsin"
            : "All optional — you can change these later in Settings";

        static string PlaceholderText => IsTurkish
            ? "Boş bırakabilirsin — sana isim verilir"
            : "Leave empty — we'll pick a name for you";

        /// <summary>
        /// Consumes pointer clicks without doing anything, so a tap on the card body
        /// stops here instead of bubbling to the backdrop's quick-confirm button.
        /// </summary>
        sealed class CardTapCatcher : MonoBehaviour, IPointerClickHandler
        {
            public void OnPointerClick(PointerEventData eventData) { }
        }
    }
}
