using System.Collections.Generic;
using Avalonia.Media;

namespace Stranichnik.Theming;

public sealed record ThemePalette(IReadOnlyDictionary<string, Color> Colors);
