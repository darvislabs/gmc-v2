# GMC v2 — Dev environment seed / status notes

## Environments
- **Dev**: this VPS. Kestrel :5062 (gmc-v2.service), SQL = docker `gmc-sql` DB `GMCv2`
  (conn string in deploy/gmc-v2.service `GMCV2_SQL`). `GMCV2_DBMODE=sql`. In-memory mode
  (`GMCV2_DBMODE=memory`) for CI/tests.
- **v1 archive**: docker `gmc-sql-v1-retired` (pending rename approval), DB `GMC` — read-only,
  never written. Backup: ~/gmc-archive-v1/GMC_v1_final.bak
- **UAT/Prod (post-G2)**: Azure per architecture §14.1 — App Service + Container Apps +
  Service Bus + Azure SQL + Blob + Redis + Key Vault + Front Door/WAF.

## Seeds
- Users (v2.0 demo auth): underwriter@opexminds.com · sales@opexminds.com ·
  admin@opexminds.com — password Demo@12345 (SHA256+salt; forced reset post-pilot).
- Authority matrix: UW ≤5% · SeniorUW ≤15% · ChiefUW ≤50% (GST-inclusive).
- Pricing master: PMV-2026Q4-SEED (trend 8/9/10.5, IBNR 3/5/8, Z 0.60, benchmark 19,000,
  margin 11, expense 9, normalization 3, star rejection 2, commission 7.5/max 15, GST 18).
- Guardrails: 15-rule standard set (db §10), DQ + mapping confidence as HardStops.

## Known build-mode boundaries (v2.0)
- AI tiers (Extraction/Mapping LLM/Clinical LLM/Narrative) are interface-stubbed, disabled.
- Vector retrieval tier deferred pending G2 measurement (plan §7 criterion 6).
- No carrier/TPA integrations; competitor upload is display-only (AD-10 fence).
