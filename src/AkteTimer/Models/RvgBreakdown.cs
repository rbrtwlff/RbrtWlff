using System.Collections.Generic;

namespace AkteTimer.Models;

public sealed record RvgLineItem(
    string Name,
    decimal Factor,
    decimal BaseFee,
    decimal Amount);

public sealed record RvgBreakdown(
    List<RvgLineItem> Items,
    decimal Total);
