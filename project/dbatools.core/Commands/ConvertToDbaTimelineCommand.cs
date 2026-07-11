#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Database;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Generates interactive HTML timeline visualizations from SQL Server job history, backup
/// history, and database growth event data. Port of public/ConvertTo-DbaTimeline.ps1 with its
/// private helpers ConvertTo-JsDate and Convert-DbaTimelineStatusColor absorbed; surface pinned
/// by migration/baselines/ConvertTo-DbaTimeline.json (no OutputType attribute — the PS source
/// declares none).
/// </summary>
[Cmdlet(VerbsData.ConvertTo, "DbaTimeline")]
public sealed class ConvertToDbaTimelineCommand : DbaBaseCmdlet
{
    /// <summary>The SQL Server data to convert into a timeline (Get-DbaAgentJobHistory, Get-DbaDbBackupHistory, or Find-DbaDbGrowthEvent output).</summary>
    [Parameter(Position = 0, Mandatory = true, ValueFromPipeline = true)]
    public object[] InputObject { get; set; } = null!;

    /// <summary>Removes the row labels showing SQL instance and item names from the left side of the timeline chart.</summary>
    [Parameter]
    public SwitchParameter ExcludeRowLabel { get; set; }

    private List<string> _body = null!;
    private List<object?> _servers = null!;
    private string? _callerName;

    protected override void BeginProcessing()
    {
        // PS: $body = $servers = @()
        _body = new List<string>();
        _servers = new List<object?>();
        _callerName = null;
    }

    protected override void ProcessRecord()
    {
        // PS has no interrupt guard in process — Test-FunctionInterrupt runs only in end{},
        // so every pipeline block still registers servers, sniffs, and can warn again.
        if (InputObject is null || InputObject.Length == 0)
        {
            return;
        }

        object? first = InputObject[0];

        // PS: if ($InputObject[0].SqlInstance -notin $servers) { $servers += ... } — array
        // addition FLATTENS an enumerable value (a collection-valued SqlInstance registers
        // every element), while the -notin test compares the value as a whole.
        object? firstSqlInstance = PsProperty.Get(first, "SqlInstance");
        if (!ListContainsPs(_servers, firstSqlInstance))
        {
            object? sqlInstanceBase = firstSqlInstance is PSObject sqlWrapped ? sqlWrapped.BaseObject : firstSqlInstance;
            if (sqlInstanceBase is not string && LanguagePrimitives.GetEnumerable(sqlInstanceBase) is IEnumerable sqlElements)
            {
                foreach (object? sqlElement in sqlElements)
                {
                    _servers.Add(sqlElement);
                }
            }
            else
            {
                _servers.Add(firstSqlInstance);
            }
        }

        List<TimelineRow> rows;
        object? firstBase = first is PSObject firstWrapped ? firstWrapped.BaseObject : first;

        if (PsOps.Eq(PsProperty.Get(first, "TypeName"), "AgentJobHistory"))
        {
            _callerName = "Get-DbaAgentJobHistory";
            rows = MapRows(MapAgentJobHistory);
        }
        else if (firstBase is BackupHistory)
        {
            // PS: the caller name is assigned WITH a leading space in the source.
            _callerName = " Get-DbaDbBackupHistory";
            rows = MapRows(MapBackupHistory);
        }
        else if (PsProperty.Has(first, "EventClass") && PsProperty.Has(first, "ChangeInSize"))
        {
            _callerName = "Find-DbaDbGrowthEvent";
            rows = MapRows(MapGrowthEvent);
        }
        else
        {
            // sorry to be so formal, can't help it ;)
            StopFunction("Unsupported input data. To request support for additional commands, please file an issue at dbatools.io/issues and we'll take a look");
            return;
        }

        // PS: $body += "$($data | ForEach-Object { "['..'],," })" — the outer expandable string
        // joins the per-row strings with the SESSION $OFS (default single space, read per
        // call like PS resolves it); ONE body string per block.
        List<string> rowStrings = new List<string>();
        foreach (TimelineRow row in rows)
        {
            rowStrings.Add(string.Format(CultureInfo.InvariantCulture,
                "['{0}','{1}','{2}',{3}, {4}],",
                row.VLabel, row.HLabel, row.Style, row.StartDate, row.EndDate));
        }
        _body.Add(string.Join(GetOfsSeparator(), rowStrings));
    }

    protected override void EndProcessing()
    {
        // PS: if (Test-FunctionInterrupt) { return }
        if (Interrupted)
        {
            return;
        }

        // PS: $body is an @() object array (its elements are strings) — the emitted middle
        // object must be object[], not string[].
        object[] bodyStrings = new object[_body.Count];
        for (int i = 0; i < _body.Count; i++)
        {
            bodyStrings[i] = _body[i];
        }
        if (_servers.Count == 1)
        {
            // PS: $body = $body -replace ("(?!\[')(\[.*?)\] ", "") — strip "[instance] " row
            // labels (the negative lookahead keeps the JS row openers that start with [').
            for (int i = 0; i < bodyStrings.Length; i++)
            {
                bodyStrings[i] = Regex.Replace((string)bodyStrings[i], "(?!\\[')(\\[.*?)\\] ", "", RegexOptions.IgnoreCase);
            }
        }

        List<string> serverParts = new List<string>();
        foreach (object? server in _servers)
        {
            serverParts.Add(server is null ? string.Empty : PSObject.AsPSObject(server).ToString());
        }

        // The raw-string constant ends at "showRowLabels:" (a trailing space would be too
        // fragile to keep in source), so the separating space is concatenated explicitly.
        string footer = FooterA
            + " "
            + (ExcludeRowLabel.ToBool() ? "false" : "true")
            + FooterB
            + _callerName
            + FooterC
            + string.Join(", ", serverParts)
            + FooterD;

        // PS: $begin, $body, $end — the pipeline flattens exactly one level, so the body array
        // itself is the middle output object (String[] with one element per process block).
        WriteObject(HeaderLiteral);
        WriteObject(bodyStrings, false);
        WriteObject(footer);
    }

    private sealed class TimelineRow
    {
        public string VLabel = "";
        public string HLabel = "";
        public string Style = "";
        public string StartDate = "";
        public string EndDate = "";
    }

    private List<TimelineRow> MapRows(Func<object?, TimelineRow> mapper)
    {
        // PS: $data = $InputObject | Select-Object @{...} — every element of the block's array
        // is mapped with the branch chosen from element [0].
        List<TimelineRow> rows = new List<TimelineRow>();
        foreach (object? element in InputObject)
        {
            rows.Add(mapper(element));
        }
        return rows;
    }

    private static TimelineRow MapAgentJobHistory(object? element)
    {
        TimelineRow row = new TimelineRow();
        // PS: { "[" + $($_.SqlInstance -replace "\\", "\\\") + "] " + $_.Job -replace "\'", '' }
        // — the trailing -replace binds to the WHOLE concatenation and strips single quotes.
        string vLabel = "[" + EscapeInstance(PsText(PsProperty.Get(element, "SqlInstance"))) + "] " + PsText(PsProperty.Get(element, "Job"));
        row.VLabel = Regex.Replace(vLabel, "\\'", "", RegexOptions.IgnoreCase);
        row.HLabel = PsText(PsProperty.Get(element, "Status"));
        row.Style = ConvertTimelineStatusColor(PsProperty.Get(element, "Status"));
        row.StartDate = ConvertToJsDate(PsProperty.Get(element, "StartDate"));
        row.EndDate = ConvertToJsDate(PsProperty.Get(element, "EndDate"));
        return row;
    }

    private static TimelineRow MapBackupHistory(object? element)
    {
        TimelineRow row = new TimelineRow();
        // PS: no quote strip here, and no Style column at all (renders as an empty field).
        row.VLabel = "[" + EscapeInstance(PsText(PsProperty.Get(element, "SqlInstance"))) + "] " + PsText(PsProperty.Get(element, "Database"));
        row.HLabel = PsText(PsProperty.Get(element, "Type"));
        row.Style = "";
        row.StartDate = ConvertToJsDate(PsProperty.Get(element, "Start"));
        row.EndDate = ConvertToJsDate(PsProperty.Get(element, "End"));
        return row;
    }

    private static TimelineRow MapGrowthEvent(object? element)
    {
        TimelineRow row = new TimelineRow();
        // PS: ("[" + escapedInstance + "] " + ($_.DatabaseName -replace "\\", "\\\")).Replace("'", "\'")
        // — the .NET Replace METHOD (ordinal, case-sensitive) ESCAPES quotes instead of stripping.
        string vLabel = "[" + EscapeInstance(PsText(PsProperty.Get(element, "SqlInstance"))) + "] " + EscapeInstance(PsText(PsProperty.Get(element, "DatabaseName")));
        row.VLabel = vLabel.Replace("'", "\\'");

        // PS: switch ([int]$_.EventClass) { 92..95 } default "Unknown"; a failing [int] cast is
        // statement-terminating in the calculated property and leaves the value empty. The
        // hLabel and Style expressions each read and convert EventClass INDEPENDENTLY (a
        // volatile getter can legitimately give them different values).
        int hLabelClass;
        bool hLabelClassKnown;
        try
        {
            hLabelClass = (int)LanguagePrimitives.ConvertTo(PsProperty.Get(element, "EventClass"), typeof(int), CultureInfo.InvariantCulture);
            hLabelClassKnown = true;
        }
        catch
        {
            hLabelClass = 0;
            hLabelClassKnown = false;
        }
        if (hLabelClassKnown)
        {
            switch (hLabelClass)
            {
                case 92: row.HLabel = "Data Grow"; break;
                case 93: row.HLabel = "Log Grow"; break;
                case 94: row.HLabel = "Data Shrink"; break;
                case 95: row.HLabel = "Log Shrink"; break;
                default: row.HLabel = "Unknown"; break;
            }
        }
        else
        {
            row.HLabel = "";
        }

        // PS: if ([int]$_.EventClass -in 92, 93) { "#36B300" } else { "#FF8C00" } — own read.
        int styleClass;
        bool styleClassKnown;
        try
        {
            styleClass = (int)LanguagePrimitives.ConvertTo(PsProperty.Get(element, "EventClass"), typeof(int), CultureInfo.InvariantCulture);
            styleClassKnown = true;
        }
        catch
        {
            styleClass = 0;
            styleClassKnown = false;
        }
        row.Style = styleClassKnown && (styleClass == 92 || styleClass == 93) ? "#36B300" : "#FF8C00";
        row.StartDate = ConvertToJsDate(PsProperty.Get(element, "StartTime"));
        row.EndDate = ConvertToJsDate(PsProperty.Get(element, "EndTime"));
        return row;
    }

    /// <summary>The session $OFS as PS expandable-string joins resolve it (default one space).</summary>
    private string GetOfsSeparator()
    {
        object? ofsValue;
        try
        {
            ofsValue = SessionState.PSVariable.GetValue("OFS");
        }
        catch
        {
            ofsValue = null;
        }
        if (ofsValue is null)
        {
            return " ";
        }
        return (string)LanguagePrimitives.ConvertTo(ofsValue, typeof(string), CultureInfo.InvariantCulture);
    }

    /// <summary>PS: $value -replace "\\", "\\\" — every backslash becomes three.</summary>
    private static string EscapeInstance(string value)
    {
        return Regex.Replace(value, "\\\\", "\\\\\\", RegexOptions.IgnoreCase);
    }

    /// <summary>PS expandable-string rendering of a property value (null becomes empty).</summary>
    private static string PsText(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }
        return PSObject.AsPSObject(value).ToString();
    }

    /// <summary>
    /// Private helper Convert-DbaTimelineStatusColor absorbed: SQL Agent status literal to html
    /// color, PS switch case-insensitivity preserved, default "#FF00CC". The helper's MANDATORY
    /// [string]$Status rejects a null/empty binding — the calculated property errors (statement
    /// class) and the Style field renders EMPTY, not magenta.
    /// </summary>
    private static string ConvertTimelineStatusColor(object? status)
    {
        if (status is null)
        {
            return string.Empty;
        }
        string text = PsText(status);
        if (text.Length == 0)
        {
            return string.Empty;
        }
        if (PsString.Eq(text, "Failed")) { return "#FF3D3D"; }
        if (PsString.Eq(text, "Succeeded")) { return "#36B300"; }
        if (PsString.Eq(text, "Retry")) { return "#FFFF00"; }
        if (PsString.Eq(text, "Canceled")) { return "#C2C2C2"; }
        if (PsString.Eq(text, "In Progress")) { return "#00CCFF"; }
        return "#FF00CC";
    }

    /// <summary>
    /// Private helper ConvertTo-JsDate absorbed: "new Date(yyyy, MM-1, dd, HH, mm, ss)" — the
    /// month is 0-based int arithmetic on the Get-Date -Format "MM" string (unpadded), the
    /// other parts keep their leading zeros. Get-Date -Format renders with the CURRENT culture
    /// (calendar included), so the formatting culture is preserved; the "MM"-1 arithmetic is
    /// PS string-to-int conversion (invariant numeric parse). A null/unconvertible input fails
    /// the [datetime] cast in PS (statement-terminating in the calculated property, empty
    /// value) — rendered empty here; a non-numeric month string leaves that component empty
    /// exactly like the failing subexpression would.
    /// </summary>
    private static string ConvertToJsDate(object? inputDate)
    {
        DateTime date;
        try
        {
            date = (DateTime)LanguagePrimitives.ConvertTo(inputDate, typeof(DateTime), CultureInfo.InvariantCulture);
        }
        catch
        {
            return string.Empty;
        }
        CultureInfo culture = CultureInfo.CurrentCulture;
        string monthComponent;
        try
        {
            monthComponent = (int.Parse(date.ToString("MM", culture), NumberStyles.Integer, CultureInfo.InvariantCulture) - 1).ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            monthComponent = string.Empty;
        }
        return string.Format(CultureInfo.InvariantCulture,
            "new Date({0}, {1}, {2}, {3}, {4}, {5})",
            date.ToString("yyyy", culture),
            monthComponent,
            date.ToString("dd", culture),
            date.ToString("HH", culture),
            date.ToString("mm", culture),
            date.ToString("ss", culture));
    }

    /// <summary>PS: $value -notin $servers (case-insensitive -eq semantics per element).</summary>
    private static bool ListContainsPs(List<object?> list, object? value)
    {
        foreach (object? entry in list)
        {
            if (PsOps.Eq(entry, value))
            {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeCrlf(string text)
    {
        // The PS here-string literals carry the checked-out file's CRLF line endings; the .cs
        // source may be checked out either way, so canonicalize.
        return text.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private static readonly string HeaderLiteral = NormalizeCrlf("""
<html>
<head>
<!-- Developed by Marcin Gminski, marcin.gminski.net, 2018 -->
<!-- Load jQuery required to autosize timeline -->
<script src="https://code.jquery.com/jquery-3.3.1.min.js" integrity="sha256-FgpCb/KJQlLNfOu91ta32o/NMZxltwRo8QtmkMRdAu8=" crossorigin="anonymous"></script>
<!-- Load Bootstrap -->
<script src="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.7/js/bootstrap.min.js" integrity="sha384-Tc5IQib027qvyjSMfHjOMaLkfuWVxZxUPnCJA7l2mCWNIpG9mGCD8wGNIcPD7Txa" crossorigin="anonymous"></script>
<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.7/css/bootstrap.min.css" integrity="sha384-BVYiiSIFeK1dGmJRAkycuHAHRg32OmUcww7on3RYdg4Va+PmSTsz/K68vbdEjh4u" crossorigin="anonymous">
<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.7/css/bootstrap-theme.min.css" integrity="sha384-rHyoN1iRsVXV4nD0JutlnGaslCJuC7uwjduW9SVrLvRYooPp2bWYgmgJQIXwl/Sp" crossorigin="anonymous">
<!-- Load Google Charts library -->
<script type="text/javascript" src="https://www.gstatic.com/charts/loader.js"></script>
<!-- a bit of custom styling to work with bootstrap grid -->
<style>

    html,body{height:100%;background-color:#c2c2c2;}
    .viewport {height:100%}

    .chart{
        background-color:#fff;
        text-align:left;
        padding:0;
        border:1px solid #7D7D7D;
        -webkit-box-shadow:1px 1px 3px 0 rgba(0,0,0,.45);
        -moz-box-shadow:1px 1px 3px 0 rgba(0,0,0,.45);
        box-shadow:1px 1px 3px 0 rgba(0,0,0,.45)
    }
    .badge-custom{background-color:#939}
    .container {
        height:100%;
    }
    .fill{
        width:100%;
        height:100%;
        min-height:100%;
        padding:10px;
    }
    .timeline-tooltip{
        border:1px solid #E0E0E0;
        font-family:Arial,Helvetica;
        font-size:10pt;
        padding:12px
    }
    .timeline-tooltip div{padding:6px}
    .timeline-tooltip span{font-weight:700}
</style>
    <script type="text/javascript">
    google.charts.load('43', {'packages':['timeline']});
    google.charts.setOnLoadCallback(drawChart);
    function drawChart() {
        var container = document.getElementById('Chart');
        var chart = new google.visualization.Timeline(container);
        var dataTable = new google.visualization.DataTable();
        dataTable.addColumn({type: 'string', id: 'vLabel'});
        dataTable.addColumn({type: 'string', id: 'hLabel'});
        dataTable.addColumn({type: 'string', role: 'style' });
        dataTable.addColumn({type: 'date', id: 'date_start'});
        dataTable.addColumn({type: 'date', id: 'date_end'});

        dataTable.addRows([
""");

    private static readonly string FooterA = NormalizeCrlf("""
]);
        var paddingHeight = 20;
        var rowHeight = dataTable.getNumberOfRows() * 41;
        var chartHeight = rowHeight + paddingHeight;
        dataTable.insertColumn(2, {type: 'string', role: 'tooltip', p: {html: true}});
        var dateFormat = new google.visualization.DateFormat({
          pattern: 'dd/MM/yy HH:mm:ss'
        });
        for (var i = 0; i < dataTable.getNumberOfRows(); i++) {
          var duration = (dataTable.getValue(i, 5).getTime() - dataTable.getValue(i, 4).getTime()) / 1000;
          var hours = parseInt( duration / 3600 ) % 24;
          var minutes = parseInt( duration / 60 ) % 60;
          var seconds = duration % 60;
          var tooltip = '<div class="timeline-tooltip"><span>' +
            dataTable.getValue(i, 1).split(",").join("<br />")  + '</span></div><div class="timeline-tooltip"><span>' +
            dataTable.getValue(i, 0) + '</span>: ' +
            dateFormat.formatValue(dataTable.getValue(i, 4)) + ' - ' +
            dateFormat.formatValue(dataTable.getValue(i, 5)) + '</div>' +
            '<div class="timeline-tooltip"><span>Duration: </span>' +
            hours + 'h ' + minutes + 'm ' + seconds + 's ';
          dataTable.setValue(i, 2, tooltip);
        }
        var options = {
            timeline: {
                rowLabelStyle: { },
                barLabelStyle: { },
                showRowLabels:
""");

    private static readonly string FooterB = NormalizeCrlf("""

            },
            hAxis: {
                format: 'dd/MM HH:mm',
            },
        }
        // Autosize chart. It would not be enough to just count rows and expand based on row height as there can be overlapping rows.
        // this will draw the chart, get the size of the underlying div and apply that size to the parent container and redraw:
        chart.draw(dataTable, options);
        // get the size of the chold div:
        var realheight= parseInt($("#Chart div:first-child div:first-child div:first-child div svg").attr( "height"))+70;
        // set the height:
        options.height=realheight
        // draw again:
        chart.draw(dataTable, options);
    }
</script>
</head>
<body>
    <div class="container-fluid">
    <div class="pull-left"><h3><code>
""");

    private static readonly string FooterC = NormalizeCrlf("""
</code> timeline for <code>
""");

    private static readonly string FooterD = NormalizeCrlf("""
</code></h3></div><div class="pull-right text-right"><img class="text-right" style="vertical-align:bottom; margin-top: 10px;" src="https://dbatools.io/wp-content/uploads/2016/05/dbatools-logo-1.png" width=150></div>
         <div class="clearfix"></div>
         <div class="col-12">
            <div class="chart" id="Chart"></div>
         </div>
         <hr>
    <p><a href="https://dbatools.io">dbatools.io</a> - the community's sql powershell module. Find us on Twitter: <a href="https://twitter.com/psdbatools">@psdbatools</a> | Chart by <a href="https://twitter.com/marcingminski">@marcingminski</a></p>
</div>
</body>
</html>
""");
}
