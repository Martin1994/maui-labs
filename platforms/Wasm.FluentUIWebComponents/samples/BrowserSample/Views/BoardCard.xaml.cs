using FluentBrowserSample.Models;
using Microsoft.Maui.Controls;

namespace FluentBrowserSample.Views;

public partial class BoardCard : Grid
{
    public BoardCard(CardDesign design)
    {
        InitializeComponent();
        BindingContext = design;
    }
}