# FVG Duo data audit

Audit date: 2026-11-11.

**Status: IN PROGRESS — the local contract-separated NQ data is available for conversion. Following the user's clarification, existing conversion and merge tooling is being investigated instead of requiring another export. This remains an inventory audit, not a completed OHLC or timestamp audit.**

## Specification and supplied files

Specification directory: `C:\Users\Administrator\Documents\Strategies\Other\FVG Duo`.

The directory contains only:

- `Spec v1.txt`
- `fvg-trading-strategy-stop-waiting-for-the-retest-10-years-tested.txt`

Neither file contains the requested minute-price dataset.

## Verified historical-data inventory

Source: `C:\Users\Administrator\Documents\NinjaTrader 8\db\minute`.

- 68 distinct NQ contract directories: quarterly expiries `03`, `06`, `09`, and `12`, for years `10` through `26` inclusive. Examples: `NQ 03-10`, `NQ 06-24`, `NQ 12-26`.
- 5,676 daily `*.Last.ncd` files across those directories. Ask and Bid files are not counted.
- Earliest filename date: `20091228`. Latest filename date: `20261030`. These are filename dates, **not decoded bar timestamps or proof of uninterrupted coverage**.
- `NQ 06-24` contains 90 Last files. Verified examples:

| Filename within NQ 06-24 | File size (bytes) |
| --- | ---: |
| 20240307.Last.ncd | 6,854 |
| 20240308.Last.ncd | 6,533 |
| 20240615.Last.ncd | 37 |
| 20240616.Last.ncd | 2,261 |

The inspected `.ncd` source is binary. Its bar records have not been decoded with a verified decoder.

`C:\Users\Administrator\Documents\NinjaTrader 8\export` is empty. No continuous export was found in the supplied specification directory or the NT8 export directory.

## Section 1a: incomplete

| Required measurement | Result |
| --- | --- |
| Source names and organization | Inventory above; contract-separated daily Last files |
| Data columns | Unverified; binary bar records not decoded |
| Bar row count | Not measured; file count is not bar count |
| First and last timestamp as written | Not decoded |
| Timestamp sorting | Not checked |
| Duplicate timestamps | Not counted |
| NaN or zero prices | Not counted |
| high >= max(open, close) violations | Not counted |
| low <= min(open, close) violations | Not counted |

## Specification gate and local-data clarification

Section 1b originally required stopping for contract-separated data. The user subsequently challenged that workflow and identified prior unattended conversion and merge work. The available data is not inherently unusable. Requiring another export before checking the existing tools was unnecessary.

Current native NT8 settings were inspected read-only:

- `Config.xml`: `TimeZoneInfoSerializable = Eastern Standard Time`.
- `Config.xml`: `GlobalMergePolicy = MergeNonBackAdjusted`.
- NQ `MasterInstruments` metadata: `TickSize = 0.25`, `PointValue = 20.0`, `MergePolicy = 3`. The numeric policy value has not been interpreted.

These settings describe the current platform configuration. They do not establish the timezone or stamp convention of the stored binary records, and they do not prove a particular Python merge policy.

Existing decoder, contract-selection rules, and prior converted datasets are being located and checked before reuse. No new decoder, roll policy, or price adjustment has been invented.

Sections 1c–1f have not yet run:

- Timezone and open/close stamp detection: not run; neither convention is assumed.
- ET-aware `open_ts` normalization: not performed.
- Missing RTH minutes and the 2% gate: not measured.
- Candidate roll dates: not computed; no `roll_dates.csv` was created.

No strategy code was added or modified for this audit. Unit tests, Phase A, Phase B, spot checks, and MT5/NT8 ports remain unexecuted. Earlier prototype or synthetic results are excluded from validation evidence.

## Continuation

Reuse established local NT8 NQ conversion and unadjusted merge tooling where its behavior and provenance can be verified. Preserve all hours and source prices. Document the actual contract-selection and timestamp conventions, then run the empirical audit gates from the specification. No additional user export is currently required.
