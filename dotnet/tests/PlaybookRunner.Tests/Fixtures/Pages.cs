// Minimal stand-ins for the NWEA pages, used by EngineIntegrationTests.
// Kept identical to ../../../nodejs/test/fixtures/pages.ts so both runners face the same pages.

using System.Text.Json;

namespace PlaybookRunner.Tests.Fixtures;

public static class Pages
{
    private static readonly Dictionary<string, string[]> Periods = new()
    {
        ["Fall 2026-2027"] =
        [
            "Fall 2026 - Winter 2027 Growth Projection Report",
            "Fall 2026 - Spring 2027 Growth Projection Report",
            "Fall 2026 - Fall 2027 Growth Projection Report",
            "Fall 2025 - Fall 2026 Growth Summary Report",
        ],
        ["Spring 2025-2026"] =
        [
            "Spring 2026 - Spring 2027 Growth Projection Report",
            "Fall 2025 - Spring 2026 Growth Summary Report",
            "Winter 2026 - Spring 2026 Growth Summary Report",
            "Spring 2025 - Spring 2026 Growth Summary Report",
        ],
    };

    /// <summary>ASG page: a System Alert, two term dropdowns, radios that change with Term Tested.</summary>
    public static readonly string Asg = $$"""
        <!doctype html><html><body>
        <div id="alert"><p>System Alert</p><button id="alert-ok" onclick="this.parentElement.remove()">OK</button></div>
        <label>Term Rostered</label><select id="rostered"><option>Fall 2026-2027</option><option>Spring 2025-2026</option></select>
        <label>Term Tested</label><select id="tested" onchange="later()"><option>Fall 2026-2027</option><option>Spring 2025-2026</option></select>
        <label>Grouping</label><select><option>None</option><option>Gender</option></select>
        <div id="periods"></div>
        <input type="button" value="Create Spreadsheet" onclick="create()">
        <script>
        const PERIODS = {{JsonSerializer.Serialize(Periods)}};
        function render() {
          const list = PERIODS[document.getElementById('tested').value];
          document.getElementById('periods').innerHTML = list.map((p, i) =>
            '<input type="radio" id="asgForm:j_id179:growthComparisonPeriod:' + i + '" name="asgForm:j_id179:growthComparisonPeriod"' +
            (i === 0 ? ' checked' : '') + ' value="' + p + '"><label for="asgForm:j_id179:growthComparisonPeriod:' + i + '">&nbsp;' + p + '</label><br>'
          ).join('');
        }
        function later() { setTimeout(render, 200); }
        function create() {
          const reports = JSON.parse(sessionStorage.getItem('reports') || '[]');
          const next = Number(sessionStorage.getItem('nextId') || 183651328); // ids only ever increase
          sessionStorage.setItem('nextId', String(next + 3));
          const id = next;
          reports.push({ id, readyAt: Date.now() + 400 });
          sessionStorage.setItem('reports', JSON.stringify(reports));
          location.href = '/queue';
        }
        render();
        </script></body></html>
        """;

    /// <summary>
    /// Reports Queue: rows become Ready to View after a moment; Remove Selected asks to confirm.
    /// Like the live site, checkboxes are custom-drawn: the real input is hidden (0x0), and a
    /// click on the drawn box (in the first cell) toggles it. A counter shows "N Reports Selected".
    /// </summary>
    public const string Queue = """
        <!doctype html><html><head><style>
          .cb input { position: absolute; width: 0; height: 0; margin: 0; opacity: 0; }
          .cb span { display: inline-block; width: 16px; height: 16px; border: 2px solid #333; }
        </style></head><body>
        <span id="count">0 Reports Selected</span> <button onclick="removeSelected()">Remove Selected</button>
        <table><thead><tr><th onclick="if (!event.target.closest('label')) flip(document.getElementById('all'))"><label class="cb"><input type="checkbox" id="all"><span></span></label></th>
        <th>DATE CREATED</th><th>REPORT TYPE</th><th>REPORT ID</th><th>STATUS</th><th>ACTION</th></tr></thead>
        <tbody id="rows"></tbody></table>
        <script>
        function boxes() { return [...document.querySelectorAll('#rows input[type=checkbox]')]; }
        function count() { document.getElementById('count').textContent = boxes().filter((b) => b.checked).length + ' Reports Selected'; }
        // A click on the cell outside the drawn box toggles it too; a click on the box uses the label.
        function flip(input) { input.checked = !input.checked; input.dispatchEvent(new Event('change', { bubbles: true })); }
        document.addEventListener('change', (e) => {
          if (e.target.id === 'all') boxes().forEach((b) => { b.checked = e.target.checked; });
          count();
        });
        const load = () => JSON.parse(sessionStorage.getItem('reports') || '[]');
        function render() {
          document.getElementById('rows').innerHTML = load().map((r) => {
            const ready = Date.now() >= r.readyAt;
            const link = ready ? '<a download="' + r.id + '.csv" href="data:text/csv,id%2Cscore%0A1%2C200%0A">Download</a>' : '';
            return '<tr><td onclick="if (!event.target.closest(\'label\')) flip(this.querySelector(\'input\'))"><label class="cb"><input type="checkbox" data-id="' + r.id + '"><span></span></label></td><td>10/25/26</td><td>Achievement Status and Growth</td>' +
              '<td>' + r.id + '</td><td>' + (ready ? 'Ready to View' : 'Processing') + '</td><td>' + link + '</td></tr>';
          }).join('');
          document.getElementById('all').checked = false;
          count();
        }
        function removeSelected() {
          if (!confirm('Remove the selected reports?')) return;
          const ids = boxes().filter((b) => b.checked).map((c) => Number(c.dataset.id));
          sessionStorage.setItem('reports', JSON.stringify(load().filter((r) => !ids.includes(r.id))));
          render();
        }
        render();
        </script></body></html>
        """;

    /// <summary>ARIA comboboxes like the Class Profile selection panel ("Term Tested", "Instructor").</summary>
    public const string Combo = """
        <!doctype html><html><body>
        <div>Term Tested</div><input role="combobox" id="term" readonly>
        <div>Instructor</div><input role="combobox" id="teacher">
        <ul id="list" role="listbox" hidden></ul>
        <p id="chosen"></p>
        <script>
        const OPTIONS = {
          term: ['Spring 2025-2026', 'Fall 2026-2027 (Most Recent)'],
          teacher: ['Abbasi, Nazneen', 'AbuFayad, Hiba', 'Daly, Louise'],
        };
        let open = null;
        function show(id, filter) {
          open = id;
          const list = document.getElementById('list');
          list.innerHTML = OPTIONS[id].filter((o) => !filter || o.toLowerCase().includes(filter.toLowerCase()))
            .map((o) => '<li role="option">' + o + '</li>').join('');
          list.hidden = false;
        }
        document.querySelectorAll('[role=combobox]').forEach((box) => {
          box.addEventListener('click', () => show(box.id));
          box.addEventListener('input', () => show(box.id, box.value));
        });
        document.getElementById('list').addEventListener('click', (e) => {
          document.getElementById(open).value = e.target.textContent;
          document.getElementById('list').hidden = true;
          document.getElementById('chosen').textContent = open + '=' + e.target.textContent;
        });
        </script></body></html>
        """;

    /// <summary>Class Profile stand-in: the RIT status card changes with the chosen course.</summary>
    public const string Class = """
        <!doctype html><html><body>
        <input type="radio" name="course" id="c1" value="Algebra 1"><label for="c1">Algebra 1</label>
        <input type="radio" name="course" id="c2" value="Reading"><label for="c2">Reading</label>
        <h3 id="status"></h3>
        <a data-testid="downloadCSVButton" download="cp.csv" href="data:text/csv,a%0A1%0A">Download .CSV</a>
        <script>
        const TEXT = { 'Algebra 1': '0 out of 22 students has an official RIT score', 'Reading': '17 out of 22 students have an official RIT score' };
        document.querySelectorAll('input[name=course]').forEach((r) =>
          r.addEventListener('change', () => { document.getElementById('status').textContent = TEXT[r.value]; }));
        </script></body></html>
        """;

    public static readonly IReadOnlyDictionary<string, string> ByPath = new Dictionary<string, string>
    {
        ["/asg"] = Asg,
        ["/queue"] = Queue,
        ["/class"] = Class,
        ["/combo"] = Combo,
    };
}
