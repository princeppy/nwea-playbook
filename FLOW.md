# NWEA Download Flow

Status: **DRAFT, waiting for approval.** Live copy for comments: https://claude.ai/artifact/YNGwACDMzea1MC4LoEn6Ph. When this flow is agreed, `nwea-class-profile.yaml` gets rewritten to follow it, along with any runner (`run.ts`) changes it needs.

Sources: the existing playbook and runner v1.4.0, your instructions, and the screen recording of 25 Oct 2026 (12:50 PM, about 9 minutes).

---

## Inputs

The playbook takes three inputs. Only the term changes from run to run.

| Input | Example | Where it comes from |
|---|---|---|
| Username | `licenses@asb.bh` | `NWEA_USER` in `.env` (or `user=` on the command line) |
| Password | `••••` | `NWEA_PASS` in `.env` (or `pass=`); always masked in logs |
| Term tested | `Fall 2026-2027` | `term="Fall 2026-2027"` on the command line (**required**) |

The term is the **season + academic year**, written exactly as NWEA shows it in the Term dropdowns:

| Term | Meaning |
|---|---|
| `Fall 2026-2027` | Fall test of academic year 2026-27 (this year) |
| `Spring 2025-2026` | Spring test of academic year 2025-26 (last year) |

- The seasons a school tests in are a playbook param, `testedSeasons`. **This school uses `[Fall, Spring]`.** A school that also tests in Winter or Summer lists those seasons too. The term's season must be one of `testedSeasons`, and the years must be consecutive. The check is `^(Fall|Winter|Spring|Summer) (\d{4})-(\d{4})$`, with end = start + 1 and the season in `testedSeasons`.
- Term Tested and Term Rostered are both set to this term on both reports. On Class Profile the playbook also accepts the same label followed by ` (Most Recent)`.
- The term label names the output folder, e.g. `downloads/nwea/Fall 2026-2027/`.
- A badly formed term, or one NWEA doesn't list, stops the run **before** any download.

```bash
npx tsx run.ts nwea-class-profile.yaml term="Fall 2026-2027"
npx tsx run.ts nwea-class-profile.yaml term="Spring 2025-2026" --headed
```

---

## Overview

| Part | Site | Result |
|---|---|---|
| A. Log in | `reports.mapnwea.org/map` → `auth.nwea.org/adult/login` | Signed-in session for every NWEA site |
| B. ASG report | `teach.mapnwea.org/report/map/asgClassReport.seam` → `reports.mapnwea.org/queue` | One CSV per Growth Comparison Period whose seasons the school tests (**3** for this school) |
| C. Class Profile | `reports.mapnwea.org/profile/class/test-details` | One CSV per teacher, subject and course **that has scored students** |

---

## Part A: Log in

1. Open `https://reports.mapnwea.org/map`. NWEA redirects to `https://auth.nwea.org/adult/login`.
2. If the **System Alert** pop-up appears (maintenance notice), click **OK**: `[data-test-id="announcement-btn"]`. It won't always show up, so this step is optional and never fails the run. When it does, it can appear before or after the email or password screen, so each login step checks for it quickly (~3 s) and moves on if it isn't there. *(new: this pop-up is in the video)*
3. Enter the email from `NWEA_USER`, then click **Continue**.
4. Enter the password from `NWEA_PASS`, then click **Sign In**.
5. Wait until the MAP Reports home page (the **Growth Reports** tab) loads.

Login locators (confirmed from DevTools screenshots, 25 Oct 2026):

| Element | Locator |
|---|---|
| Email input | `[data-test-id="input-field-login-email"]` (`#login-email`) |
| Continue button | `[data-test-id="continue-btn"]` |
| Password input | `[data-test-id="password-input-field-root"] input[type="password"]` |
| Sign in button | `[data-test-id="login-btn"]` |
| System Alert OK | `[data-test-id="announcement-btn"]` |

> The login is shared by `reports.mapnwea.org` and `teach.mapnwea.org`, so we only sign in once.

---

## Part B: Achievement Status and Growth (ASG) report, one file per tested period

This report is **not** downloaded directly. NWEA puts it in a **Reports Queue**, builds it in the background, and offers it for download once it is ready.

The page always offers 4 *Growth Comparison Period* radio options, and they depend on the term. **A period is kept only if every season it mentions is in `testedSeasons`.** For this school (`[Fall, Spring]`) that drops the Winter periods and leaves 3 files per term. A school that tests in all four seasons gets all 4.

For **each** kept period, do the following:

1. Open `https://teach.mapnwea.org/report/map/asgClassReport.seam`.
2. **Term Rostered** and **Term Tested**: the input term.
3. **Instructor**: all instructors are ticked by default. Keep them all.
4. **Report Options**:
   - **Growth Comparison Period**: click the radio for this period, then wait for the page's AJAX refresh to finish. Clicking the radio triggers `A4J.AJAX.Submit` and a "please wait" modal.
   - **Optional Grouping**: `None` (default). **Small Group Display**: unticked (default).
5. Click **Create Spreadsheet** and wait for "Generating report, please stand by…" to finish. The browser then moves to `https://reports.mapnwea.org/queue`.
6. On the **Reports Queue**, wait until the newest *Achievement Status and Growth* row shows **Ready to View** (reload while waiting), then click **Download**. NWEA names the file with a number, such as `183651328.csv`.
7. Save it as `downloads/nwea/<Term>/ASG - <radio label>.csv` (flat folder).
8. **Mandatory:** once the file is saved, tick that row and click **Remove Selected**. That way the queue never holds an old report that could be mistaken for the next one.

**Locating the radios.** They are JSF inputs whose ids contain generated parts (`asgForm:j_id179:growthComparisonPeriod:0`), and `j_id179` can change between releases. So the playbook selects them with
`input[type="radio"][name$=":growthComparisonPeriod"]`. The radio's `value` holds the short period (`Spring 2026 - Spring 2027`), and its `<label for=…>` holds the full text used in the file name.

What the page offers, from your screenshots:

| Term | Growth Comparison Period | Kept? |
|---|---|---|
| Fall 2026-2027 | Fall 2026 - Winter 2027 Growth Projection Report | ✗ Winter |
| Fall 2026-2027 | Fall 2026 - Spring 2027 Growth Projection Report | ✓ |
| Fall 2026-2027 | Fall 2026 - Fall 2027 Growth Projection Report | ✓ |
| Fall 2026-2027 | Fall 2025 - Fall 2026 Growth Summary Report | ✓ |
| Spring 2025-2026 | Spring 2026 - Spring 2027 Growth Projection Report | ✓ |
| Spring 2025-2026 | Fall 2025 - Spring 2026 Growth Summary Report | ✓ |
| Spring 2025-2026 | Winter 2026 - Spring 2026 Growth Summary Report | ✗ Winter |
| Spring 2025-2026 | Spring 2025 - Spring 2026 Growth Summary Report | ✓ |

> The labels are read from the page, never hard-coded. Which periods are kept is decided only by `testedSeasons`, so the same playbook serves Fall/Spring schools and schools that also test in Winter and Summer. Each school just sets its own list.
>
> A file that already exists is skipped, so re-runs are cheap.

---

## Part C: Class Profile CSVs, teacher → subject → course

1. Open `https://reports.mapnwea.org/profile/class/test-details` and wait for the **Change selection** button.
2. **Term Tested** and **Term Rostered**: both set to the input term.
3. For **each Instructor**, then **each Subject** (Mathematics, Language Arts, Science), then **each Course** (Math K-12, Algebra 1, …):
   1. Select Term, Instructor, Subject and Course in the panel and click **Update** (or **Cancel** if Update is greyed out because nothing changed).
   2. Wait for the report to load. The "RIT Score Status" card reads **"X out of Y students have an official RIT score"**.
   3. **If X = 0, skip this course and don't download.** *(new: this matches what you do in the video)*
   4. If X > 0, click **Download .CSV** and save it to
      `downloads/nwea/<Term>/<Teacher> - <Subject> - <Course>.csv` (flat folder)
   5. If the file already exists, skip the course without reloading the report.
4. If a single course fails, take a screenshot, reload the page and carry on with the next course. One failure doesn't stop the run.

> **Why the skip rule matters:** of the 203 CSVs downloaded on 17 Oct, **155 contained no scored students** ("No Test Results" only). With the skip rule the run is faster and only produces useful files.

---

## Output folder layout (flat)

Every CSV for a term goes in **one folder**, with no subfolders. Names follow your existing `aaa/` copy.

```
downloads/nwea/Fall 2026-2027/
├── ASG - Fall 2026 - Spring 2027 Growth Projection Report.csv
├── ASG - Fall 2026 - Fall 2027 Growth Projection Report.csv
├── ASG - Fall 2025 - Fall 2026 Growth Summary Report.csv
├── Abbasi Nazneen - Mathematics - Algebra 1.csv
├── Abbasi Nazneen - Language Arts - Reading.csv
└── …
runs/<timestamp>/        screenshots of any failures + run.log
```

---

## Runner changes this flow needs (run.ts → v1.5.0)

| New step or option | Used for |
|---|---|
| `clickIfVisible` | Dismiss the optional System Alert pop-up during login |
| `forEach` over **radio buttons** (`optionsOf` given as a radio group) | Loop over the 4 Growth Comparison Periods |
| `waitForText` with polling and reload | Wait for **Ready to View** on the Reports Queue (the report takes time to build) |
| `skipIf` *text matches* | Skip a course when the page shows "0 out of … students" |
| `download` from a **queue row** | Click the Download link on the newest row |
| Log file in `runs/<timestamp>/run.log` and an end-of-run summary | Counts of downloaded, skipped (exists), skipped (no scores) and failed |

---

## Open questions (please answer before the rewrite)

- ~~**Q1. Which terms?**~~ Answered: one term per run, passed in as `term="Fall 2026-2027"` (see Inputs).
- ~~**Q2. ASG periods.**~~ Answered: periods are filtered by the school's `testedSeasons` param. For this school (`[Fall, Spring]`) that gives 3 files per term; Winter and Summer schools get their own periods (see Part B).
- ~~**Q3. Clean the Reports Queue**~~ Answered: mandatory. Each report is removed right after its file is saved.
- **Q4. Skip rule (recommended):** skip when the card reads "0 out of Y students", including 0 out of 0. Each skip is logged in `run.log` with teacher, subject, course and the card text. Skipped courses aren't saved, so the next run checks them again and picks up scores that arrive late (NWEA can take up to 48 hours to process them).
- **Q5. Class (recommended):** keep Teacher → Subject → Course for now. The Class dropdown is greyed out in the video and in the 17 Oct screenshot, so NWEA appears to pick the class itself, and each CSV already has a `ClassName` column. On the first `--headed` run we check whether Class becomes selectable for a teacher with two classes (e.g. Homeroom 9A/9B). If it does, Class becomes a loop level and the file name becomes `Teacher - Class - Subject - Course.csv`.
- **Q6. Old downloads (recommended):** archive rather than delete. Move the nested `<Teacher>/<Subject>/` folders and `aaa/` to `downloads/nwea/_archive/2026-10-17/`, so the new flat folder starts clean and nothing is lost.
