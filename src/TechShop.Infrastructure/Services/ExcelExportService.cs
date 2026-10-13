using System.Globalization;
using System.Linq.Expressions;
using ClosedXML.Excel;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;

namespace TechShop.Infrastructure.Services;

/// <summary>
/// Builds the workbook from the same whitelist the grid uses, so an export always mirrors
/// the columns, order and rows the user is looking at.
/// </summary>
public sealed class ExcelExportService : IExcelExportService
{
    public byte[] Build<T>(string sheetName, IReadOnlyList<T> rows, QuerySpec<T> spec, IEnumerable<string> columns)
    {
        var fields = spec.ResolveColumns(columns);
        var readers = fields
            .Select(f => (Field: f, Read: Compile(f.Selector)))
            .ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(Sanitise(sheetName));

        for (var c = 0; c < readers.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = readers[c].Field.Header;
        }

        var header = sheet.Range(1, 1, 1, Math.Max(readers.Count, 1));
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
        header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < readers.Count; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                var value = readers[c].Read(rows[r]!);

                switch (value)
                {
                    case null:
                        break;
                    case decimal money:
                        cell.Value = money;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                        break;
                    case double number:
                        cell.Value = number;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                        break;
                    case int or long or short:
                        cell.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        cell.Style.NumberFormat.Format = "#,##0";
                        break;
                    case DateTime date:
                        cell.Value = date;
                        cell.Style.NumberFormat.Format = "yyyy-mm-dd hh:mm";
                        break;
                    case bool flag:
                        cell.Value = flag ? "Yes" : "No";
                        break;
                    case Enum enumValue:
                        cell.Value = enumValue.ToString();
                        break;
                    default:
                        cell.Value = value.ToString();
                        break;
                }
            }
        }

        sheet.SheetView.FreezeRows(1);

        if (rows.Count > 0 && readers.Count > 0)
        {
            sheet.Range(1, 1, rows.Count + 1, readers.Count).SetAutoFilter();
        }

        sheet.Columns().AdjustToContents(1, 200, 8d, 48d);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Func<object, object?> Compile(LambdaExpression selector)
    {
        var parameter = Expression.Parameter(typeof(object), "row");
        var typed = Expression.Convert(parameter, selector.Parameters[0].Type);
        var body = new Rebinder(selector.Parameters[0], typed).Visit(selector.Body)!;

        return Expression
            .Lambda<Func<object, object?>>(Expression.Convert(body, typeof(object)), parameter)
            .Compile();
    }

    /// <summary>Excel refuses some characters and caps the sheet name at 31 characters.</summary>
    private static string Sanitise(string name)
    {
        var cleaned = new string(name.Where(c => !"[]:*?/\\".Contains(c, StringComparison.Ordinal)).ToArray());
        cleaned = string.IsNullOrWhiteSpace(cleaned) ? "Export" : cleaned.Trim();
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }

    private sealed class Rebinder(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
