using System.Text;
using ClosedXML.Excel;
using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.Core;

public enum ExportScope
{
    EntireScan,
    SelectedFolderOnly
}

public sealed class ExportService
{
    public void ExportCsv(string filePath, FolderNode root, ExportScope scope, FolderNode? selected)
    {
        var rows = CollectRows(root, scope, selected);
        var sb = new StringBuilder();
        sb.AppendLine("Path;Subject;IdentityKind;AceType;Inheritance;Level;RightsRaw;Note");
        foreach (var row in rows)
        {
            sb.Append(Escape(row.Path)).Append(';')
              .Append(Escape(row.Subject)).Append(';')
              .Append(Escape(row.IdentityKind)).Append(';')
              .Append(Escape(row.AceType)).Append(';')
              .Append(Escape(row.Inheritance)).Append(';')
              .Append(Escape(row.Level)).Append(';')
              .Append(Escape(row.RightsRaw)).Append(';')
              .Append(Escape(row.Note))
              .AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public void ExportXlsx(string filePath, FolderNode root, ExportScope scope, FolderNode? selected)
    {
        var rows = CollectRows(root, scope, selected);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("ACL");
        sheet.Cell(1, 1).Value = "Path";
        sheet.Cell(1, 2).Value = "Subject";
        sheet.Cell(1, 3).Value = "IdentityKind";
        sheet.Cell(1, 4).Value = "AceType";
        sheet.Cell(1, 5).Value = "Inheritance";
        sheet.Cell(1, 6).Value = "Level";
        sheet.Cell(1, 7).Value = "RightsRaw";
        sheet.Cell(1, 8).Value = "Note";
        sheet.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.Path;
            sheet.Cell(r, 2).Value = row.Subject;
            sheet.Cell(r, 3).Value = row.IdentityKind;
            sheet.Cell(r, 4).Value = row.AceType;
            sheet.Cell(r, 5).Value = row.Inheritance;
            sheet.Cell(r, 6).Value = row.Level;
            sheet.Cell(r, 7).Value = row.RightsRaw;
            sheet.Cell(r, 8).Value = row.Note;
            r++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    private static IEnumerable<ExportRow> CollectRows(FolderNode root, ExportScope scope, FolderNode? selected)
    {
        if (scope == ExportScope.SelectedFolderOnly)
        {
            var node = selected ?? root;
            return RowsForNode(node, includeChildren: false);
        }

        return Flatten(root).SelectMany(n => RowsForNode(n, includeChildren: false));
    }

    private static IEnumerable<FolderNode> Flatten(FolderNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var n in Flatten(child))
                yield return n;
        }
    }

    private static IEnumerable<ExportRow> RowsForNode(FolderNode node, bool includeChildren)
    {
        if (!string.IsNullOrEmpty(node.Error) && node.Aces.Count == 0)
        {
            yield return new ExportRow(
                node.FullPath,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                node.Error);
        }

        foreach (var ace in node.Aces)
        {
            yield return new ExportRow(
                node.FullPath,
                ace.IdentityDisplayName,
                ace.IdentityKind.ToString(),
                ace.AceType == Models.AceType.Allow ? "Allow" : "Deny",
                ace.IsInherited ? "Inherited" : "Explicit",
                ace.LevelDisplayName,
                ace.RightsRaw,
                ace.Note ?? string.Empty);
        }

        if (includeChildren)
        {
            foreach (var child in node.Children)
            {
                foreach (var row in RowsForNode(child, includeChildren: true))
                    yield return row;
            }
        }
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(';') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return value;
    }

    private sealed record ExportRow(
        string Path,
        string Subject,
        string IdentityKind,
        string AceType,
        string Inheritance,
        string Level,
        string RightsRaw,
        string Note);
}
