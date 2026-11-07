using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mwrites.Dialogs;

public partial class SymbolDialog : Window
{
    private static readonly string[] Symbols =
    {
        "©", "®", "™", "€", "£", "¥", "¢", "$", "‰", "§", "¶", "•",
        "°", "±", "×", "÷", "≠", "≈", "≤", "≥", "∞", "√", "∑", "∆",
        "→", "←", "↑", "↓", "↔", "⇒", "⇔", "✓", "✗", "★", "☆", "♥",
        "“", "”", "‘", "’", "«", "»", "–", "—", "…", "·", "µ", "Ω",
        "α", "β", "γ", "δ", "π", "σ", "λ", "ε", "½", "¼", "¾", "¬"
    };

    public string? SelectedSymbol { get; private set; }

    public SymbolDialog()
    {
        InitializeComponent();
        SymbolList.ItemsSource = Symbols;
        SymbolList.SelectedIndex = 0;
    }

    private void OnInsert(object sender, RoutedEventArgs e)
    {
        Commit();
    }

    private void OnSymbolDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Commit();
    }

    private void Commit()
    {
        if (SymbolList.SelectedItem is not string symbol) return;
        SelectedSymbol = symbol;
        DialogResult = true;
    }
}
