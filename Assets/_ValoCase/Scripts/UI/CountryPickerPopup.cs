using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.Core;

namespace ValoCase.UI
{
    /// <summary>
    /// The country picker. One implementation, opened from first-launch setup and from
    /// Settings, so the two can never drift into showing different lists or different
    /// labels. Built at runtime with no prefab dependency, same pattern as the other
    /// popups in this folder.
    ///
    /// The list is virtualised: a small pool of rows is repositioned and rebound as the
    /// view scrolls, instead of instantiating 249 rows with 249 TMP meshes on every open.
    /// The catalog is small enough that the naive version would work, but it would cost a
    /// visible hitch on a low-end phone every time the picker opens, which is exactly the
    /// kind of stutter a setup screen cannot afford.
    ///
    /// The first row is "AA - AA", <see cref="CountryCatalog.NoCountryCode"/> — the code
    /// for a player who would rather not say. It is an ordinary catalog entry, so nothing
    /// here special-cases it; it is highlighted on open for an account that has no country
    /// stored at all, which is what an older save looks like.
    ///
    /// Nothing is committed until the player taps a row: the picker never invokes its
    /// callback on open or on close.
    /// </summary>
    public sealed class CountryPickerPopup : MonoBehaviour
    {
        static readonly Color Backdrop   = new Color(0f, 0f, 0f, 0.85f);
        static readonly Color CardBg     = new Color(0.051f, 0.067f, 0.090f, 1f);
        static readonly Color InputBg    = new Color(0.086f, 0.106f, 0.137f, 1f);
        static readonly Color RowBg      = new Color(0.078f, 0.098f, 0.129f, 1f);
        static readonly Color RowBgSel   = new Color(0.161f, 0.075f, 0.098f, 1f);
        static readonly Color Accent     = new Color(1f, 0.275f, 0.333f, 1f);
        static readonly Color AccentDim  = new Color(1f, 0.275f, 0.333f, 0.28f);
        static readonly Color TextMain   = new Color(0.961f, 0.961f, 0.961f, 1f);
        static readonly Color TextDim    = new Color(0.541f, 0.569f, 0.651f, 1f);

        const float CardWidth   = 760f;
        const float CardHeight  = 1240f;
        const float RowHeight   = 84f;
        const float RowSpacing  = 6f;
        const float ListPadding = 10f;
        const float ListHeight  = 940f;

        /// <summary>
        /// Row objects kept alive. ListHeight / (RowHeight + RowSpacing) rounds up to 11
        /// rows on screen; one spare at each end keeps a partially scrolled row from
        /// appearing blank.
        /// </summary>
        const int PoolSize = 13;

        static CountryPickerPopup _instance;

        readonly List<Country> _filtered = new List<Country>();
        readonly List<Row> _rows = new List<Row>(PoolSize);

        Action<string> _onPicked;
        string         _currentCode;
        TMP_InputField _search;
        RectTransform  _content;
        TextMeshProUGUI _emptyLbl;
        int            _firstBound = -1;

        struct Row
        {
            public RectTransform   Rt;
            public Image           Bg;
            public Outline         Outline;
            public TextMeshProUGUI Label;
            public int             Index;
        }

        // ── Entry point ───────────────────────────────────────────────────────

        /// <summary>
        /// Opens the picker under <paramref name="parent"/>. <paramref name="onPicked"/>
        /// fires only on an explicit row tap, with the ISO code; closing without choosing
        /// leaves the caller's current selection untouched.
        /// </summary>
        public static void Show(Transform parent, string currentCode, Action<string> onPicked)
        {
            if (_instance != null || parent == null) return;

            var go = new GameObject("CountryPickerPopup");
            _instance = go.AddComponent<CountryPickerPopup>();
            // No country set reads as the AA row being the current one, so the list always
            // opens with something highlighted instead of looking like a list nobody has
            // ever touched.
            var normalized = CountryCatalog.Normalize(currentCode);
            _instance._currentCode = normalized.Length > 0 ? normalized : CountryCatalog.NoCountryCode;
            _instance._onPicked    = onPicked;
            go.transform.SetParent(parent, false);
            _instance.BuildUi();
            go.transform.SetAsLastSibling();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ── Interaction ───────────────────────────────────────────────────────

        // Releases the search box before the popup goes away. Destroying a focused
        // TMP_InputField leaves two things behind it never cleans up: the mobile soft
        // keyboard it opened, and an EventSystem selection pointing at an object that no
        // longer exists. Either one can stop the next field the player taps — the
        // nickname box on the panel underneath — from taking focus at all.
        void ReleaseSearchFocus()
        {
            if (_search != null)
            {
                _search.DeactivateInputField();
                _search.ReleaseSelection();
            }

            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null &&
                es.currentSelectedGameObject.transform.IsChildOf(transform))
            {
                es.SetSelectedGameObject(null);
            }
        }

        void Close()
        {
            ReleaseSearchFocus();
            Destroy(gameObject);
        }

        void OnRowClicked(int index)
        {
            if (index < 0 || index >= _filtered.Count) return;
            var picked = _filtered[index].Code;
            var callback = _onPicked;
            _onPicked = null;              // one selection per open
            Close();
            callback?.Invoke(picked);
        }

        void OnSearchChanged(string query)
        {
            CountryCatalog.Search(query, _filtered);
            _firstBound = -1;
            _content.sizeDelta = new Vector2(0f, ContentHeight());
            _content.anchoredPosition = Vector2.zero;
            if (_emptyLbl != null) _emptyLbl.gameObject.SetActive(_filtered.Count == 0);
            BindVisibleRows();
        }

        float ContentHeight() =>
            _filtered.Count == 0 ? 0f : ListPadding * 2f + _filtered.Count * (RowHeight + RowSpacing) - RowSpacing;

        // Rebinds the pool to whichever slice of the filtered list the viewport now shows.
        // Called on every scroll frame, so it does no work when the slice has not moved.
        void BindVisibleRows()
        {
            int first = 0;
            if (_filtered.Count > 0)
            {
                float scrolled = Mathf.Max(0f, _content.anchoredPosition.y - ListPadding);
                first = Mathf.Clamp(Mathf.FloorToInt(scrolled / (RowHeight + RowSpacing)),
                                    0, Mathf.Max(0, _filtered.Count - 1));
            }
            if (first == _firstBound) return;
            _firstBound = first;

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                int index = first + i;
                bool used = index < _filtered.Count;
                if (row.Rt.gameObject.activeSelf != used) row.Rt.gameObject.SetActive(used);
                if (!used) { row.Index = -1; _rows[i] = row; continue; }

                var country = _filtered[index];
                bool selected = string.Equals(country.Code, _currentCode, StringComparison.Ordinal);

                row.Index = index;
                row.Rt.anchoredPosition = new Vector2(0f, -(ListPadding + index * (RowHeight + RowSpacing)));
                row.Label.text  = country.Label;
                row.Label.color = selected ? Accent : TextMain;
                row.Bg.color    = selected ? RowBgSel : RowBg;
                row.Outline.effectColor = selected ? Accent : AccentDim;
                _rows[i] = row;
            }
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
            var dimBtn = gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Close);

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Outline));
            card.transform.SetParent(transform, false);
            var cardRt = (RectTransform)card.transform;
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(CardWidth, CardHeight);
            card.GetComponent<Image>().color = CardBg;
            var cardOl = card.GetComponent<Outline>();
            cardOl.effectColor    = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
            cardOl.effectDistance = new Vector2(2f, -2f);

            var title = UIBuild.MakeTmp(card.transform, "Title", TitleText, 34f, FontStyles.Bold, Accent);
            title.alignment        = TextAlignmentOptions.Center;
            title.characterSpacing = 4f;
            TopBand(title.rectTransform, -40f, 46f);

            BuildCloseButton(card.transform);
            BuildSearchInput(card.transform);
            BuildList(card.transform);

            _emptyLbl = UIBuild.MakeTmp(card.transform, "Empty", EmptyText, 22f, FontStyles.Normal, TextDim);
            _emptyLbl.alignment = TextAlignmentOptions.Center;
            TopBand(_emptyLbl.rectTransform, -240f, 40f);
            _emptyLbl.gameObject.SetActive(false);

            OnSearchChanged(string.Empty);
        }

        static void TopBand(RectTransform rt, float y, float height, float sidePad = 40f)
        {
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta        = new Vector2(-sidePad * 2f, height);
        }

        void BuildCloseButton(Transform parent)
        {
            var go = new GameObject("CloseBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-18f, -18f);
            rt.sizeDelta        = new Vector2(64f, 64f);
            go.GetComponent<Image>().color = InputBg;

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            UIBuild.WireButtonClick(btn);
            btn.onClick.AddListener(Close);

            var lbl = UIBuild.MakeTmp(go.transform, "Lbl", "×", 40f, FontStyles.Bold, TextDim);
            lbl.alignment = TextAlignmentOptions.Center;
            UIBuild.Stretch(lbl.rectTransform);
        }

        void BuildSearchInput(Transform parent)
        {
            var wrap = new GameObject("SearchWrap", typeof(RectTransform), typeof(Image), typeof(Outline));
            wrap.transform.SetParent(parent, false);
            TopBand((RectTransform)wrap.transform, -104f, 80f, 40f);
            wrap.GetComponent<Image>().color = InputBg;
            var wrapOl = wrap.GetComponent<Outline>();
            wrapOl.effectColor    = AccentDim;
            wrapOl.effectDistance = new Vector2(1.5f, -1.5f);

            var fieldGo = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            fieldGo.transform.SetParent(wrap.transform, false);
            UIBuild.Stretch((RectTransform)fieldGo.transform);
            fieldGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var taGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            taGo.transform.SetParent(fieldGo.transform, false);
            var taRt = (RectTransform)taGo.transform;
            taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(24f, 8f); taRt.offsetMax = new Vector2(-24f, -8f);

            var ph = UIBuild.MakeTmp(taGo.transform, "Placeholder", SearchHintText, 24f, FontStyles.Italic, TextDim);
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            UIBuild.Stretch(ph.rectTransform);

            var txt = UIBuild.MakeTmp(taGo.transform, "Text", "", 24f, FontStyles.Bold, TextMain);
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            UIBuild.Stretch(txt.rectTransform);

            var field = fieldGo.GetComponent<TMP_InputField>();
            field.textViewport  = taRt;
            field.textComponent = txt;
            field.placeholder   = ph;
            field.contentType   = TMP_InputField.ContentType.Standard;
            field.caretColor    = Accent;
            field.text          = string.Empty;
            field.onValueChanged.AddListener(OnSearchChanged);
            _search = field;
        }

        void BuildList(Transform parent)
        {
            var scrollGo = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            TopBand((RectTransform)scrollGo.transform, -204f, ListHeight, 40f);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal        = false;
            scroll.vertical          = true;
            scroll.movementType      = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.onValueChanged.AddListener(_ => BindVisibleRows());

            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            scroll.viewport = vpRt;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(vpRt, false);
            _content = (RectTransform)contentGo.transform;
            _content.anchorMin        = new Vector2(0f, 1f);
            _content.anchorMax        = new Vector2(1f, 1f);
            _content.pivot            = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta        = Vector2.zero;
            scroll.content            = _content;

            for (int i = 0; i < PoolSize; i++) _rows.Add(BuildRow(i));
        }

        Row BuildRow(int poolIndex)
        {
            var go = new GameObject($"Row_{poolIndex}",
                typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(_content, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-16f, RowHeight);

            var bg = go.GetComponent<Image>();
            bg.color = RowBg;

            var ol = go.GetComponent<Outline>();
            ol.effectColor    = AccentDim;
            ol.effectDistance = new Vector2(1f, -1f);

            var lbl = UIBuild.MakeTmp(go.transform, "Lbl", string.Empty, 26f, FontStyles.Bold, TextMain);
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            var lRt = lbl.rectTransform;
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
            lRt.offsetMin = new Vector2(24f, 0f); lRt.offsetMax = new Vector2(-24f, 0f);

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            UIBuild.WireButtonClick(btn);
            int captured = poolIndex;
            btn.onClick.AddListener(() => OnRowClicked(_rows[captured].Index));

            go.SetActive(false);
            return new Row { Rt = rt, Bg = bg, Outline = ol, Label = lbl, Index = -1 };
        }

        // ── Player-facing text ────────────────────────────────────────────────
        // Device language, matching how NicknameMessages picks its wording. The project
        // has no localization system; these three strings are not a reason to add one.

        static bool IsTurkish => Application.systemLanguage == SystemLanguage.Turkish;

        static string TitleText      => IsTurkish ? "ÜLKE SEÇ" : "SELECT COUNTRY";
        static string SearchHintText => IsTurkish ? "Ülke veya kod ara…" : "Search country or code…";
        static string EmptyText      => IsTurkish ? "Eşleşen ülke yok." : "No matching country.";
    }
}
