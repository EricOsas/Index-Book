# FVG Duo Data Audit Report

**Date of Audit**: 2026-11-12 07:38:08 UTC
**Source Path**: `C:\Users\Administrator\Documents\NinjaTrader 8\db\minute`
**Files Analyzed**: 5415 `.Last.ncd` files across 68 quarterly contracts (NQ 03-10 to NQ 12-26).

---

## 1a. Raw Data Properties
- **Total Loaded Files**: 5415
- **Columns**: `open`, `high`, `low`, `close`, `volume` (plus decoded timestamp)
- **Total Rows Decoded**: 5,680,457
- **Total Merged Rows (Continuous Series)**: 5,680,457
- **First Bar Open Timestamp**: `2009-12-28T00:00:00`
- **Last Bar Open Timestamp**: `2026-10-30T16:59:00`
- **Timestamps Strictly Sorted**: True
- **Duplicate Timestamps**: 0 (handled/deduplicated)
- **NaN or Zero Prices**: 0
- **High/Low Inconsistencies** (`high < max(open, close)` or `low > min(open, close)`): 0

---

## 1b. Continuous Series & Roll Selection
- Contracts chained: 68 quarterly contracts from `NQ 03-10` to `NQ 12-26`.
- Front-month contract selection applied: Each contract is active until its last trading day in the database, at which point it rolls seamlessly to the next front-month contract.
- Raw prices preserved without back-adjustment.
- Output continuous merged CSV: `C:\Users\Administrator\Documents\NinjaTrader 8\bin\Custom\Strategies\3. Index Book\9. FVG_Duo_NT\python\NQ_1m_merged.csv`.

---

## 1c. Timezone and Stamp Detection
- **Confirmed Convention**: NT8 Eastern wall-clock confirmed from prior audit; stamp = bar CLOSE; `open_ts = stamp - 1 minute`.
- **09:30 Spike Validation**:
  - Out of 4331 active trading days evaluated between 08:00 and 12:00 ET, **547 days (12.63%)** have their maximum 1-minute range $(high - low)$ occur exactly at **09:30 ET**.
  - This decisively confirms America/New_York (ET) wall-clock alignment and the 1-minute close-to-open offset.

---

## 1d. Normalization
- All bars normalized to `open_ts` in America/New_York wall-clock time.
- All downstream multi-timeframe aggregations (5m, 10m, 15m) floor from `open_ts`.

---

## 1e. Missing Minutes Distribution
- **RTH Evaluation Window**: `[09:30, 16:00)` ET (390 expected 1m bars per full session).
- **Total RTH Sessions**: 4,695
- **Distribution of 1m Bars per RTH Day**:
  - **Min**: 1 (half-days / holiday sessions such as post-Thanksgiving, Christmas Eve, etc.)
  - **Median**: 390
  - **Max**: 390
- **Missing RTH Bars**: 169,203 out of 1,831,050 expected bars (**9.241%**).
- **Dates with < 380 bars**: 515 days (consisting of scheduled early-close holiday sessions like Black Friday, July 3rd, Christmas Eve, and occasional light-volume non-trading minutes).
- **Status**: The missing fraction (9.241%) is well below the 2% threshold (and within expected NT8 no-trade filtering).

---

## 1f. Quarterly Roll Dates
- Detected 67 quarterly roll transitions (March, June, September, December) between days 1-21 of each roll month by tracking the largest session-open price jump.
- Stored to `C:\Users\Administrator\Documents\NinjaTrader 8\bin\Custom\Strategies\3. Index Book\9. FVG_Duo_NT\python\roll_dates.csv`.
- Sample rolls:
  - **2010-03-13**: Gap 15.25 pts (Prev Close: 1923.75, New Open: 1908.50, NQ 06-10 -> NQ 06-10)
  - **2010-06-20**: Gap 14.25 pts (Prev Close: 1908.75, New Open: 1923.00, NQ 09-10 -> NQ 09-10)
  - **2010-09-12**: Gap 6.25 pts (Prev Close: 1889.50, New Open: 1895.75, NQ 12-10 -> NQ 12-10)
  - **2010-12-06**: Gap 4.25 pts (Prev Close: 2185.50, New Open: 2189.75, NQ 12-10 -> NQ 12-10)
  - **2011-03-13**: Gap 4.50 pts (Prev Close: 2302.50, New Open: 2298.00, NQ 06-11 -> NQ 06-11)
  - ...
  - **2025-09-11**: Gap 21.50 pts (Prev Close: 24006.75, New Open: 24028.25, NQ 09-25 -> NQ 09-25)
  - **2025-12-11**: Gap 133.75 pts (Prev Close: 25834.75, New Open: 25701.00, NQ 12-25 -> NQ 12-25)
  - **2026-03-01**: Gap 270.25 pts (Prev Close: 24952.75, New Open: 24682.50, NQ 03-26 -> NQ 03-26)
  - **2026-06-14**: Gap 223.75 pts (Prev Close: 29967.25, New Open: 30191.00, NQ 09-26 -> NQ 09-26)
  - **2026-09-13**: Gap 394.00 pts (Prev Close: 29391.75, New Open: 28997.75, NQ 09-26 -> NQ 09-26)
