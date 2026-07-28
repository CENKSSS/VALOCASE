using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Profile;

namespace ValoCase.UI
{
    /// <summary>
    /// One-time first-launch profile setup: new players pick a nickname and avatar
    /// before playing. Shown by GameContext once the session can persist the choice
    /// (after backend boot sync, or immediately in local editor mode). Built at
    /// runtime with zero prefab dependency, same pattern as NoInternetPopup.
    /// Completion is stored in SaveDataRoot.profileSetupCompleted.
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
        static readonly Color TextMain   = new Color(0.961f, 0.961f, 0.961f, 1f);
        static readonly Color TextDim    = new Color(0.541f, 0.569f, 0.651f, 1f);
        static readonly Color DarkText   = new Color(0.043f, 0.055f, 0.082f, 1f);

        TMP_InputField  _nameInput;
        TextMeshProUGUI _errorLbl;
        Button          _confirmBtn;
        Image           _confirmImg;
        TextMeshProUGUI _confirmLbl;
        Transform       _grid;
        string          _selectedKey;
        Sprite          _selectedSprite;
        bool            _saving;

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

            var parent = FindPopupParent();
            if (parent == null)
            {
                Debug.LogWarning("[FirstLaunchProfile] No Canvas found — popup skipped this session.");
                Destroy(gameObject);
                yield break;
            }

            ProfileManager.EnsureInitialized();
            transform.SetParent(parent, false);
            BuildUi();
            transform.SetAsLastSibling();
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

        void OnConfirmClicked()
        {
            if (_saving) return;
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Save?.Data == null) return;

            if (!TryValidateNickname(_nameInput != null ? _nameInput.text : null,
                    out var nickname, out var error))
            {
                ShowError(error);
                return;
            }

            ShowError(null);
            SetSaving(true);

            if (ctx.BackendEnabled)
            {
                ctx.SaveDisplayNameBackend(nickname,
                    _ => SaveAvatarThenFinish(ctx),
                    err =>
                    {
                        ShowError(string.IsNullOrEmpty(err) ? "Could not save. Please try again." : err);
                        SetSaving(false);
                    });
                return;
            }

            PlayerProfileData.SetUsername(nickname);
            if (_selectedSprite != null) PlayerProfileData.SetAvatar(_selectedSprite, _selectedKey);
            ctx.Save.Data.playerName = nickname;
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
            Debug.Log($"[FirstLaunchProfile] Setup complete — name={PlayerProfileData.Username} avatar={_selectedKey}");
            Destroy(gameObject);
        }

        // Mirrors the backend rules used by SettingsScreen: 3–20 chars, letters/digits/underscore.
        static bool TryValidateNickname(string raw, out string trimmed, out string error)
        {
            trimmed = (raw ?? string.Empty).Trim();
            error = null;

            if (string.IsNullOrEmpty(trimmed)) { error = "Nickname cannot be empty.";           return false; }
            if (trimmed.Length < 3)            { error = "Nickname must be at least 3 characters."; return false; }
            if (trimmed.Length > 20)           { error = "Nickname must be at most 20 characters."; return false; }

            foreach (var c in trimmed)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                          (c >= '0' && c <= '9') || c == '_';
                if (!ok) { error = "Only letters, digits and _ are allowed."; return false; }
            }
            return true;
        }

        void ShowError(string message)
        {
            if (_errorLbl != null) _errorLbl.text = message ?? string.Empty;
        }

        void SetSaving(bool saving)
        {
            _saving = saving;
            if (_confirmBtn != null) _confirmBtn.interactable = !saving;
            if (_confirmLbl != null) _confirmLbl.text = saving ? "SAVING..." : "CONFIRM";
            if (_confirmImg != null) _confirmImg.color = saving ? AccentDim : Accent;
            if (_nameInput  != null) _nameInput.interactable = !saving;
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

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Outline));
            card.transform.SetParent(transform, false);
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
                "Pick a nickname and avatar to get started", 20f, FontStyles.Normal, TextDim);
            subtitle.alignment = TextAlignmentOptions.Center;
            TopBand(subtitle.rectTransform, -98f, 30f);

            var nameHint = UIBuild.MakeTmp(card.transform, "NameHint", "NICKNAME", 18f, FontStyles.Bold, TextDim);
            nameHint.alignment        = TextAlignmentOptions.MidlineLeft;
            nameHint.characterSpacing = 3f;
            TopBand(nameHint.rectTransform, -158f, 26f, 60f);

            BuildNameInput(card.transform);

            var avHint = UIBuild.MakeTmp(card.transform, "AvatarHint", "SELECT AVATAR", 18f, FontStyles.Bold, TextDim);
            avHint.alignment        = TextAlignmentOptions.MidlineLeft;
            avHint.characterSpacing = 3f;
            TopBand(avHint.rectTransform, -316f, 26f, 60f);

            BuildAvatarScrollGrid(card.transform);
            PopulateAvatars();

            _errorLbl = UIBuild.MakeTmp(card.transform, "Error", "", 20f, FontStyles.Normal, Accent);
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

            var ph = UIBuild.MakeTmp(taGo.transform, "Placeholder", "Enter nickname…", 26f, FontStyles.Italic, TextDim);
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            UIBuild.Stretch(ph.rectTransform);

            var txt = UIBuild.MakeTmp(taGo.transform, "Text", "", 26f, FontStyles.Bold, TextMain);
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            UIBuild.Stretch(txt.rectTransform);

            _nameInput = fieldGo.GetComponent<TMP_InputField>();
            _nameInput.textViewport   = taRt;
            _nameInput.textComponent  = txt;
            _nameInput.placeholder    = ph;
            _nameInput.characterLimit = 20;
            _nameInput.contentType    = TMP_InputField.ContentType.Standard;
            _nameInput.caretColor     = Accent;
            _nameInput.text           = "Agent";
            _nameInput.onValueChanged.AddListener(_ => ShowError(null));
        }

        void BuildAvatarScrollGrid(Transform parent)
        {
            var scrollGo = new GameObject("AvatarScroll",
                typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            TopBand(scrollRt, -350f, 560f, 50f);
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
                ShowError(null);
                RefreshCellHighlights();
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
        }
    }
}
