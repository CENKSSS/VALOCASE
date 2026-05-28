using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI.Screens
{
    /// <summary>Per-player UI references: name label, VP label, roulette and history grid.</summary>
    public sealed class CaseBattlePanelRefs
    {
        public TextMeshProUGUI  NameLabel;
        public TextMeshProUGUI  VpLabel;

        // Roulette strip
        public RectTransform    RouletteContainer;
        public RectTransform    RouletteContent;
        public List<Image>      RouletteCards = new List<Image>();

        public RectTransform ReelViewport;
        public RectTransform ReelContent;

        public Image CenterFrame;
        public Image CenterGlow;

        // Permanent round-result grid
        public Transform        GridRoot;
        public List<GameObject> HistoryCards  = new List<GameObject>();

        // Premium redesign refs
        public GameObject RouletteAreaObject;
        public Image      PanelBackground;
        public Image      HeaderGlow;

        // Multi-bot lobby — used to re-anchor columns at battle start
        public RectTransform ColumnRect;
    }

    /// <summary>All UI element references produced by CaseBattleUiBuilder.Build().</summary>
    public sealed class CaseBattleUiRefs
    {
        // Cost strip
        public TextMeshProUGUI CostAmountLabel;
        public TextMeshProUGUI CaseCountLabel;
        public Transform       CaseIconsRow;

        // Title bar
        public TextMeshProUGUI RoundLabel;
        public RectTransform   TopBarRect;

        // Player panels (Opponent = Bot1)
        public CaseBattlePanelRefs Player   = new CaseBattlePanelRefs();
        public CaseBattlePanelRefs Opponent = new CaseBattlePanelRefs();
        public CaseBattlePanelRefs Bot2     = new CaseBattlePanelRefs();
        public CaseBattlePanelRefs Bot3     = new CaseBattlePanelRefs();

        // VS badge GameObject (hidden when player count > 2)
        public GameObject VsBadge;

        // Footer
        public TextMeshProUGUI PlayerTotalLabel;
        public TextMeshProUGUI OpponentTotalLabel;
        public GameObject      WinnerBadge;
        public TextMeshProUGUI WinnerNameLabel;

        // Lobby overlay
        public GameObject LobbyOverlay;
        public readonly List<(Button btn, int count, Image bg, Outline ol)> CountButtons
            = new List<(Button, int, Image, Outline)>();
        public Button          StartButton;
        public TextMeshProUGUI LobbyCostLabel;
        public TextMeshProUGUI LobbyBalanceLabel;

        // Action bar
        public GameObject ActionBar;
        public Button     PlayAgainButton;
        public Button     InventoryButton;

        // Profile avatar (updated live when profile changes)
        public Image PlayerAvatarImg;

        // ── New flow refs (Setup / CasePicker / Arena container) ─────────
        public GameObject       SetupPanel;
        public Button           AddCasesButton;
        public Transform        SelectedCasesRoot;
        public GameObject       CasePickerPanel;
        public Transform        CasePickerGridRoot;
        public Button           DoneButton;
        public Button           CreateGameButton;
        public Button           EditCasesButton;  // tapping selected-cases area re-opens picker
        public readonly List<(Button btn, int count, Image bg, Outline ol)> PlayerCountButtons
            = new List<(Button, int, Image, Outline)>();
        public TextMeshProUGUI  TotalCostLabel;
        public TextMeshProUGUI  TotalCasesLabel;
        public GameObject       ArenaPanel;

        // Final result popup / warning popup (same overlay, different content)
        public GameObject       FinalPopup;
        public CanvasGroup      FinalPopupCanvasGroup;   // explicit raycast gate
        public TextMeshProUGUI  FinalPopupTitleLabel;
        public TextMeshProUGUI  FinalPopupBodyLabel;     // draw-only middle line
        public TextMeshProUGUI  FinalPopupTotalLabel;
        public Button           FinalPopupOkButton;
        public Button           FinalPopupPlayAgainButton; // draw-only "TEKRAR OYNA"
    }
}
