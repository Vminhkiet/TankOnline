# TODO - Shop Ownership Sync (Backend + Client)

- [ ] Backend: Add `GET /api/shop/my-items` endpoint (read purchased item ids by player).
- [ ] Backend: Extend `GameService` + `ShopServiceImpl` with purchased-item query method.
- [ ] Client: Update `TankPurchaseManager` to load ownership from `/api/shop/my-items` (server source of truth).
- [ ] Client: Keep Buy/Owned UI in sync after purchase + refresh ownership data.
- [ ] Run API + critical-path verification and report status.
