using System.Globalization;
using System.Windows.Data;
using Finpulse.Domain;

namespace Finpulse.Desktop;

public sealed class MovementKindConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is MovementKind.Income ? "Ingreso" : "Gasto";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
