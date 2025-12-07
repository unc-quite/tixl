using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using T3.Core.Utils;
using T3.Serialization;

namespace T3.Editor.UiModel.Selection;

internal sealed class TourPoint
{
    public string Description = string.Empty;
    public Guid Id { get; internal init; }
    public Guid ChildId;
    public Guid InputId;

    [JsonConverter(typeof(SafeEnumConverter<Styles>))]
    public Styles Style = Styles.Info;

    public enum Styles
    {
        Info,
        InfoFor,
        CallToAction,
        Conclusion,
        Tip,
    }

    internal TourPoint Clone()
    {
        return new TourPoint
                   {
                       Id = Guid.NewGuid(),
                       Description = Description,
                       Style = Style,
                   };
    }

    public void ToMarkdown(StringBuilder sb, SymbolUi symbolUi)
    {
        // Type with reference to child and input
        sb.Append($"## {Style}");
        if (ChildId != Guid.Empty && TryGetChildNameIndex(symbolUi, ChildId, out var childName))
        {
            sb.Append("(");
            sb.Append(childName);
            sb.Append(") ");
        }

        // Write id
        sb.Append(" &");
        sb.Append(Id.ShortenGuid());
        sb.AppendLine();
        sb.AppendLine(Description.Replace("\n", "\n\n"));
        sb.AppendLine();
        
    }

    private static bool TryGetChildNameIndex(SymbolUi symbolUi, Guid id, [NotNullWhen(true)] out string name)
    {
        name = "FAIL?";
        
        if (!symbolUi.ChildUis.TryGetValue(id, out var child))
        {
            return false;
        }

        // find identical children and order them by height / pos x
        var childSymbolId = child.SymbolChild.Symbol.Id;
        var index = symbolUi.ChildUis.Values
                            .Where(c => c.SymbolChild.Symbol.Id == childSymbolId && c.CollapsedIntoAnnotationFrameId == Guid.Empty)
                            .OrderBy(c => c.PosOnCanvas.Y + c.PosOnCanvas.X * 0.1f)
                            .Select((item, idx) => new { Item = item, Index = idx })
                            .FirstOrDefault(pair => pair.Item.Id == child.Id)?.Index ?? -1;

        if (index == -1)
        {
            return false;
        }

        if (index == 0)
        {
            name= child.SymbolChild.Symbol.Name;
            return true;
        }
        
        name= $"{child.SymbolChild.Symbol.Name}:{index+1}";
        return true;
    }
}