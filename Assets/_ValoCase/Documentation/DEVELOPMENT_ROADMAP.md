# Development Roadmap

## Phase 1 — Foundation (current)

- [x] Service architecture (VP, inventory, cases, shop, daily, stats, save)
- [x] ScriptableObject data pipeline
- [x] JSON local save
- [x] UI screen navigation skeleton
- [x] Weighted rarity drop tables
- [x] Reel spin controller with skip
- [x] Editor sample content generator

## Phase 2 — Visual & UX polish

- [ ] Build all prefabs from `PREFABS.md` + wire references
- [ ] Custom VALORANT-inspired UI sprites (original art only for portfolio)
- [ ] Particle/VFX for Exclusive & Ultra reveals
- [ ] Case opening result modal with share screenshot
- [ ] Animated menu background (shader or video loop)
- [ ] Full DOTween integration on all transitions

## Phase 3 — Content expansion

- [ ] 50+ skins across collections
- [ ] 8+ case types with unique drop tables
- [ ] Collection completion bonuses
- [ ] Case unlock progression UI
- [ ] Limited-time cases with countdown

## Phase 4 — Mobile production

- [ ] Addressables for content
- [ ] Android IL2CPP release build
- [ ] Performance profiling (UI batching, pool sizes)
- [ ] Cloud save (optional, Firebase)
- [ ] Analytics hooks (Unity Gaming Services)

## Phase 5 — Portfolio packaging

- [ ] Record gameplay trailer
- [ ] README + architecture diagram
- [ ] Itch.io / GitHub pages demo build
- [ ] Clear non-commercial / fan disclaimer

## Drop rate tuning

Edit `CaseDropTableSO` rarity weights. Target feel:

| Rarity | Standard crate | Premium crate |
|--------|----------------|---------------|
| Select | 52% | 38% |
| Deluxe | 28% | 30% |
| Premium | 14% | 20% |
| Exclusive | 5% | 9% |
| Ultra | **1%** | **3%** |

Ultra skins should remain **&lt; 2%** on standard crates.
