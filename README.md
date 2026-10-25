# playbook-lab: NWEA MAP report downloader

Logs in to NWEA MAP once and downloads, for one term:

1. **ASG reports**: the *Achievement Status and Growth* CSV for every Growth Comparison Period the school tests. That is **3 files per term** for this school.
2. **Class Profile CSVs**: one per **teacher → subject → course** that has at least one student with an official RIT score.

All CSVs for a term go in **one flat folder**. A rerun skips files that already exist, so it only fetches what is missing.

The browser steps live in a YAML **playbook** (`nwea-class-profile.yaml`). A runner carries them out. There are two runners, both built on Playwright. They behave the same: same steps, inputs, log lines, summary and exit codes.

- `nodejs/`: Node.js / TypeScript
- `dotnet/`: C# / .NET 10

The playbook, `.env`, `downloads/` and `runs/` stay in the repo root, so both runners share them. Use either one.

---

## Quick start

```bash
./nodejs/run.sh                          # term from NWEA_TERM in .env
./nodejs/run.sh "Spring 2025-2026"       # a different term for this run
./nodejs/run.sh "Fall 2026-2027" --headed   # watch the browser
./nodejs/run.sh test                     # run the test suite
```

The C# runner takes the same arguments:

```bash
./dotnet/run.sh                          # term from NWEA_TERM in .env
./dotnet/run.sh "Fall 2026-2027" --headed
./dotnet/run.sh test
```

Each `run.sh` works from any folder: it installs or builds in its own folder, then runs from the repo root. On the first run, `nodejs/run.sh` installs the npm packages and Playwright's Chromium; `dotnet/run.sh` builds the project and installs Chromium. After that, `dotnet/run.sh` rebuilds incrementally on every run, which is quick when nothing has changed.

### Run with `npx` from any folder (no clone, no install)

```bash
cd ~/somewhere
npx github:princeppy/nwea-playbook "Spring 2025-2026" --headed
```

`npx` fetches the latest `main` from GitHub. From a clone, `npx /path/to/playbook-lab` (or `.../nodejs`) runs your local copy instead.

It works in the current folder: `.env` is read from it, and `downloads/` and `runs/` are written to it. The repo's `nwea-class-profile.yaml` is used unless you name another playbook. Without a `.env`, it asks for the username and password at the terminal (the password is not shown); the term is then the first argument. Chromium is installed on first use.

The root `package.json` exists only for this: it names the command (`nodejs/bin/nwea-playbook.mjs`) and its runtime packages. All code and the Node development setup stay in `nodejs/`; keep the `dependencies` in both files in step.

**Requirements:** Node.js 22 or newer (`nodejs/`), or the .NET 10 SDK (`dotnet/`); macOS/Linux shell.

### `.env` (never committed)

```bash
NWEA_USER="you@school.org"        # NWEA login
NWEA_PASS="your-password"
NWEA_TERM="Fall 2026-2027"        # default term: season + academic year exactly as NWEA shows it
```

### Options

| Option | Effect |
|---|---|
| `"Fall 2026-2027"` (first argument) | Term for this run, overriding `NWEA_TERM` |
| `term="…"` | Same, as a name=value pair |
| `user=… pass=…` | Override `NWEA_USER` / `NWEA_PASS` for one run. They stay masked in the logs, but they end up in your shell history, so prefer `.env` |
| `--headed` | Show the browser (slowed to 200 ms per action) |
| `--keep` | Show the browser and leave it open at the end (Ctrl+C to quit) |
| `--slow=300` | Custom slow-motion delay in ms |

Without `run.sh` (from the repo root): `nodejs/node_modules/.bin/tsx nodejs/run.ts nwea-class-profile.yaml term="Fall 2026-2027" [--headed]`, or `npm run nwea` inside `nodejs/`.

### Exit codes

`0` everything done · `1` stopped by an error (bad input, login failure, …) · `2` finished, but some items failed. The summary lists them.

---

## Inputs and rules

| Input | Source | Notes |
|---|---|---|
| Username / password | `NWEA_USER` / `NWEA_PASS` in `.env`, or `user=` / `pass=`; if neither, the Node runner asks at the terminal | Checked before the browser opens; masked in console and `run.log` |
| Term tested | `NWEA_TERM` in `.env`, or the first argument / `term=` | Required |

**Term format:** season + academic year, exactly as in NWEA's Term dropdowns:

| Term | Meaning |
|---|---|
| `Fall 2026-2027` | Fall test of academic year 2026-27 |
| `Spring 2025-2026` | Spring test of academic year 2025-26 |

- The run checks the term **before logging in**. It must match `Season YYYY-YYYY`, use consecutive years, and use a season in `testedSeasons`.
- **`testedSeasons`** is set in the playbook. It is `[Fall, Spring]` for this school, which tests twice a year. A school that also tests in Winter or Summer adds those seasons, and its runs then include the matching ASG periods.
- Term Tested and Term Rostered are both set to the input term on both reports. On Class Profile, `Fall 2026-2027` also matches `Fall 2026-2027 (Most Recent)`.

---

## What a run does

### A. Log in, then empty the Reports Queue

1. Open `https://reports.mapnwea.org/map`, which redirects to `https://auth.nwea.org/adult/login`.
2. Enter the email, click **Continue**, enter the password, click **Sign in**.
3. Wait for the MAP Reports home page.

The **System Alert** maintenance pop-up doesn't always appear. It is listed under `dismiss:` in the playbook, so the runner clicks its **OK** whenever it is visible: before every step and while waiting. It never fails the run.

| Element | Locator |
|---|---|
| Email | `[data-test-id="input-field-login-email"]` |
| Continue | `[data-test-id="continue-btn"]` |
| Password | `[data-test-id="password-input-field-root"] input[type="password"]` |
| Sign in | `[data-test-id="login-btn"]` |
| System Alert OK | `[data-test-id="announcement-btn"]` |

The same login also covers `teach.mapnwea.org`.

**Then, before any ASG or Class Profile report:** the run opens `https://reports.mapnwea.org/queue` and **removes every report in it**. It ticks the select-all box (or each row, if that misses some), clicks **Remove Selected**, and checks the queue is empty. That way an old report can never be taken for a new one. A report that won't go is logged and ignored.

The queue's checkboxes are custom-drawn: the real `<input>` is hidden (seen live on 25 Oct 2026). So the run first tries a normal check, then clicks the drawn box in the row's first cell, and confirms with the page's "*N Reports Selected*" counter.

### B. ASG report (one file per tested Growth Comparison Period)

NWEA doesn't download this report directly. It builds the report in the **Reports Queue**, and the run downloads it from there.

For each Growth Comparison Period that the season filter keeps (the queue is already empty, see A):
1. Open `https://teach.mapnwea.org/report/map/asgClassReport.seam`.
2. Set **Term Rostered** and **Term Tested** to the term. The run sets every term `<select>` on the page, repeats until none change, and requires at least 2.
3. Keep all instructors ticked (the page default).
4. Click the period's radio button and wait for the page's AJAX refresh. Optional Grouping stays `None` and Small Group Display stays unticked.
5. Click **Create Spreadsheet** and wait to land on `reports.mapnwea.org/queue` (up to 5 min).
6. Reload the queue until the **new** *Achievement Status and Growth* row says **Ready to View** (up to 15 min, checked every 15 s). The new row is the one with the highest Report ID that wasn't left over from before.
7. Click its **Download**, then save it as `ASG - <period>.csv`.
8. **Mandatory:** tick the row, click **Remove Selected**, accept any confirmation and check the row is gone. That way an old report can never be mistaken for the next one.

The period labels are read from the page; none are hard-coded. A period is kept only if **every season it names** is in `testedSeasons`:

| Term | Growth Comparison Period | Kept for `[Fall, Spring]`? |
|---|---|---|
| Fall 2026-2027 | Fall 2026 - Winter 2027 Growth Projection Report | No (Winter) |
| Fall 2026-2027 | Fall 2026 - Spring 2027 Growth Projection Report | Yes |
| Fall 2026-2027 | Fall 2026 - Fall 2027 Growth Projection Report | Yes |
| Fall 2026-2027 | Fall 2025 - Fall 2026 Growth Summary Report | Yes |
| Spring 2025-2026 | Spring 2026 - Spring 2027 Growth Projection Report | Yes |
| Spring 2025-2026 | Fall 2025 - Spring 2026 Growth Summary Report | Yes |
| Spring 2025-2026 | Winter 2026 - Spring 2026 Growth Summary Report | No (Winter) |
| Spring 2025-2026 | Spring 2025 - Spring 2026 Growth Summary Report | Yes |

The period radios are found with `input[type="radio"][name$=":growthComparisonPeriod"]`. Their JSF ids contain a generated part (`asgForm:j_id179:…`) that can change. The file name comes from each radio's `<label for>` text.

### C. Class Profile CSVs (teacher → subject → course)

1. Open `https://reports.mapnwea.org/profile/class/test-details`.
2. For every **Instructor**, then every **Subject**, then every **Course**:
   1. If `<Teacher> - <Subject> - <Course>.csv` already exists, skip the course at once.
   2. Select Term Tested, Term Rostered, Instructor, Subject and Course in the selection panel, then click **Update**, or **Cancel** if Update is greyed out.
   3. Wait for the page to settle, and for the report subtitle to end with `| <Course>` so it isn't still showing the previous course. That subtitle check is optional and only logged if not seen.
   4. Read the RIT Score Status card, "*X out of Y students have an official RIT score*". **If X = 0, skip the course.** No file is written, and the skip is logged with the card text. The next run checks the course again, because NWEA can take up to 48 hours to process scores.
   5. Otherwise click **Download .CSV** and save the file.
3. If a teacher, subject or course fails, the run takes a screenshot, reloads the page and continues with the next one.

**Class:** the loop is teacher → subject → course. The Class dropdown is greyed out (NWEA picks the class), and each CSV has a `ClassName` column. See [First live run](#first-live-run-checklist).

---

## Output

```
downloads/nwea/Fall 2026-2027/
├── ASG - Fall 2026 - Spring 2027 Growth Projection Report.csv
├── ASG - Fall 2026 - Fall 2027 Growth Projection Report.csv
├── ASG - Fall 2025 - Fall 2026 Growth Summary Report.csv
├── Abbasi Nazneen - Mathematics - Algebra 1.csv
├── Abbasi Nazneen - Language Arts - Reading.csv
└── …
runs/<timestamp>/
├── run.log            every step, every skip (with reason), the summary
└── *.png              a screenshot for each failure
```

- Names are cleaned for the file system: `Abbasi, Nazneen` becomes `Abbasi Nazneen`, and `/ : * ? " < > |` become `-`.
- Downloads are written to `<file>.part` and then renamed, so an interrupted run never leaves a half-written CSV that a later run would skip.
- At the end of a run, the console and `run.log` show:

```
Summary
  downloaded: 52
  skipped (already downloaded): 3
  skipped (no scored students): 148
  failed: 1
    - course "Geometry": Timed out after 30000 ms waiting for ...
```

`downloads/` and `runs/` are git-ignored.

---

## Playbook reference

`nwea-class-profile.yaml` has these top-level keys:

| Key | Purpose |
|---|---|
| `name`, `description` | Shown in the log |
| `params` | Inputs: `term: { required: true, env: NWEA_TERM }`, `testedSeasons: [Fall, Spring]`, `downloadDir`. Command line beats `env:`, which beats the default. |
| `secrets` | `NWEA_USER: { arg: user }`: read from `.env`, or from `user=` on the command line. All are checked at start. |
| `dismiss` | Pop-ups to click whenever visible |
| `locators` | YAML anchors (`&name`) reused below |
| `recover` | Default steps after a failed loop item |
| `steps` | The run |

Use `${name}` for any param or loop variable. In file paths, loop variables are cleaned for the file system.

### Steps

| Step | Example | Does |
|---|---|---|
| `goto` | `goto: https://…` | Open a URL (waits for the page content, 60 s) |
| `waitFor` | `waitFor: { testid: x, timeout: 60000, optional: true }` | Wait until visible. With `optional`, the step logs instead of failing. |
| `waitForIdle` | `waitForIdle:` | Wait until no page or AJAX request has been in flight for 0.5 s (15 s max) |
| `waitForUrl` | `waitForUrl: { url: "**/queue**", timeout: 300000 }` | Wait for navigation |
| `click` / `fill` | `fill: { css: …, secret: NWEA_USER }` | Click; type a value or secret |
| `choose` | `choose: { in: { field: Instructor }, option: "${teacher}" }` | Pick from a combobox. `X` also matches `X (Most Recent)`. Long lists are filtered by typing. |
| `setAllSelects` | `setAllSelects: { optionMatches: '^(Fall\|…) \d{4}-\d{4}', option: "${term}", min: 2 }` | Set every native `<select>` offering a matching option |
| `clickIfEnabled` | `clickIfEnabled: { target: …, otherwise: … }` | Click if enabled, else click `otherwise` |
| `clickIfVisible` | `clickIfVisible: { target: …, timeout: 3000 }` | Click only if it appears in time |
| `download` | `download: { click: …, saveAs: "…" }` | Click and save the download |
| `skipIfExists` | `skipIfExists: { file: "…", cleanup: [...] }` | Skip this loop item if the file exists |
| `skipIfText` | `skipIfText: { target: …, matches: '^0 out of', reason: … }` | Skip this loop item when the text matches |
| `checkTerm` | `checkTerm: { term: "${term}", seasons: "${testedSeasons}" }` | Validate the term; stop early if it is invalid |
| `nweaQueueClear` | `nweaQueueClear: {}` | On the queue page: remove every report; any that won't go are ignored from then on |
| `nweaQueueDownload` | `nweaQueueDownload: { report: …, saveAs: "…", timeout, poll }` | Wait for the new report, download it, remove it from the queue |
| `screenshot` / `sleep` | `sleep: 1000` | Screenshot into `runs/`; pause |
| `forEach` | see below | Loop |

### `forEach`

```yaml
- forEach:
    as: course                       # sets ${course} (and ${course_name}, with strip: applied)
    optionsOf: { field: Course }     # items = a dropdown's options …
    # radiosOf: { css: … }           # … or a radio group (named by <label for>)
    onlySeasons: "${testedSeasons}"  # optional: drop items naming an untested season
    skip: [ "Some item" ]            # optional: names to leave out
    limit: 2                         # optional: first N items only (handy for testing)
    onError: continue                # log, screenshot, recover, next item (default: stop the run)
    recover: [ ... ]                 # optional: overrides the top-level recover:
    before: [ ... ]                  # runs before the list is opened, for every item
    do: [ ... ]                      # runs after the item is picked
    close: [ ... ]                   # runs if the list turns out to be empty
```

The list is read once. Each later item runs `before` again, re-opens the list and picks the item **by name**, so a list that changes order can't shift what is picked.

### Locators

| Form | Meaning |
|---|---|
| `"text"` or `{ text: …, exact: true }` | By visible text |
| `{ css: … }`, `{ testid: … }` | CSS selector, `data-testid` |
| `{ role: button, name: Update }` | ARIA role and accessible name |
| `{ regex: '\d+ out of \d+' }` | Text matching a regular expression |
| `{ endsWith: "| ${course}" }` | Text ending with exactly this; special characters are literal |
| `{ field: Instructor }` | The first combobox after a label with that text |
| `{ any: [ …, … ] }` | Whichever alternative is found first |
| `nth: 2` | Pick one of several matches |

---

## Project layout

```
nwea-class-profile.yaml    the NWEA playbook (shared by the runners)
package.json               lets `npx github:princeppy/nwea-playbook` run nodejs/bin/nwea-playbook.mjs
.env                       login and default term (shared, git-ignored)
downloads/, runs/          CSVs, run logs, screenshots (shared, git-ignored)
nodejs/
  run.sh                 entry point for people (checks Node, installs on first run, passes the term)
  run.ts                 entry point for the runner: inputs, secrets, browser, summary, exit code
  bin/nwea-playbook.mjs  what `npx` runs; src/launch.ts its arguments
  package.json, tsconfig.json
  src/engine.ts          runs steps and forEach loops
  src/sources.ts         forEach item sources: dropdown options, radio buttons
  src/steps/             step types: navigation, input, files, nwea, queue-ui (ticking/removing queue rows), save (safe downloads)
  src/context.ts         shared step context; pop-up dismissal; waiting
  src/locators.ts        YAML locator → Playwright locator; choose()
  src/term.ts            term and season rules         src/queue.ts    queue-row parsing
  src/params.ts          params (CLI > env > default)  src/secrets.ts  secrets and masking
  src/prompt.ts          asks at the terminal for secrets missing from .env and the command line
  src/network.ts         AJAX-quiet detection          src/stats.ts    summary counts
  src/args.ts, logger.ts, text.ts, vars.ts            CLI parsing, logging, text and ${var} helpers
  test/                  unit tests + browser tests against local stand-in pages (test/fixtures)
dotnet/
  run.sh                 entry point for people (checks .NET, builds, installs Chromium on first run, passes the term)
  PlaybookLab.slnx, global.json, Directory.Build.props
  src/PlaybookRunner/    the runner; one C# file per Node module, same names and same step types:
    Program.cs           entry point: inputs, secrets, browser, summary, exit code (--install-browsers too)
    Engine.cs, Sources.cs, Context.cs, Locators.cs, Steps/*.cs, Terms.cs, QueueRows.cs, Params.cs,
    Secrets.cs, NetworkTracker.cs, Stats.cs, RunLog.cs, CliArgs.cs, TextUtil.cs, Vars.cs
    StepArg.cs, YamlLoader.cs   YAML values (typed as the Node yaml package types them); DotEnv.cs  .env reader
  tests/PlaybookRunner.Tests/  xUnit v3: the Node tests ported, plus step and start-up tests (Fixtures/Pages.cs
                         holds the same stand-in pages as nodejs/test/fixtures)
```

### Development

Node.js:

```bash
cd nodejs
npm test            # 41 tests: unit + integration (headless Chromium, no network)
npm run coverage    # coverage report (about 96% of lines)
npm run typecheck   # tsc, strict
```

C#:

```bash
cd dotnet
dotnet test --solution PlaybookLab.slnx            # 66 tests: unit + integration (headless Chromium, no network)
dotnet test --solution PlaybookLab.slnx --coverage --coverage-output-format cobertura   # about 86% of hand-written lines
dotnet build src/PlaybookRunner -c Release         # warnings are errors (Directory.Build.props)
```

`global.json` selects the Microsoft.Testing.Platform test runner, which the .NET 10 SDK requires for xUnit v3.

The integration tests serve small stand-in pages from memory: an ASG page with a System Alert and term dropdowns, a Reports Queue with confirmation and a stale report, comboboxes, and a Class Profile card. They exercise the full step engine without touching NWEA. Both runners use the same pages; if you change one copy, change the other.

The runner's version and change history are at the top of `nodejs/run.ts` (currently **v1.5.0**). The C# runner tracks the same version number at the top of `dotnet/src/PlaybookRunner/Program.cs`. A change to a step should be made in both runners.

---

## First live run checklist

These details come from the screen recording and DevTools screenshots of 25 Oct 2026, but haven't been tested against the live site yet. Run `./nodejs/run.sh --headed` once and check:

- [x] **ASG term dropdowns** are native `<select>` elements, set by `setAllSelects` (confirmed 25 Oct 2026).
- [x] **Create Spreadsheet** lands on `reports.mapnwea.org/queue`, and the new report becomes Ready to View (confirmed).
- [ ] **Instructors** stay ticked after the term changes on the ASG page. Check that the ASG CSV covers all teachers.
- [x] **Queue rows** are table rows with a Report ID, "Ready to View" and **Download**. Confirmed on 25 Oct 2026, and the download works.
- [ ] **Queue checkboxes and Remove Selected**: the select-all/row ticking via the drawn box, and any confirmation. In the first live run the plain `check()` timed out; the click-the-box fallback has been added since.
- [ ] **Class dropdown**: for a teacher with two classes (e.g. Homeroom 9A and 9B), is Class selectable? If it is, add a Class loop level and put the class in the file name.
- [ ] **RIT status card** reads "X out of Y students have an official RIT score", and the report subtitle ends with the course name.

A quick trial: add `limit: 1` under `as: teacher` (and under `as: period`) in the playbook.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| `Missing required input(s): term …` | Set `NWEA_TERM` in `.env` or pass a term |
| `Term "…" is not valid` / `only tests in Fall and Spring` | Write the term as NWEA shows it; check `testedSeasons` |
| `Missing secret(s): NWEA_USER …` | Add it to `.env`, or run it at a terminal so it can ask (not from a script or cron) |
| Run stops at login | Run with `--headed`; check `runs/<timestamp>/fatal.png` and `run.log` |
| `"…" was not ready in the Reports Queue after 15 min` | NWEA is slow; raise `timeout:` under `nweaQueueDownload`, then rerun (finished files are skipped) |
| `report N is still in the Reports Queue` | The file was saved; remove the row by hand |
| Many `(page still busy …, continuing)` lines | Usually harmless; NWEA keeps background requests open |
| Need a fresh copy of everything | Delete `downloads/nwea/<term>/` and run again |
