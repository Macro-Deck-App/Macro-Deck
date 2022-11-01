using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class RoundedTextBox : UserControl
{
    private int borderRadius = 8;
    private Color placeHolderColor = Color.Gray;
    private string placeHolderText = "";
    private bool isPlaceHolder;
    private bool isPasswordChar;
    private Image icon;

    public Image Icon
    {
        get => icon;
        set
        {
            icon = value;
            Padding = icon == null ? new Padding(8, 5, 8, 5) : new Padding(textBox1.Height + 8 + 3, 5, 8, 5);
            Invalidate();
        }
    }

    public ScrollBars ScrollBars
    {
        get => textBox1.ScrollBars;
        set => textBox1.ScrollBars = value;
    }

    public bool ReadOnly
    {
        get => textBox1.ReadOnly;
        set => textBox1.ReadOnly = value;
    }

    public HorizontalAlignment TextAlignment {
        get => textBox1.TextAlign;
        set => textBox1.TextAlign = value;
    }
        
    public bool PasswordChar
    {
        get => isPasswordChar;
        set
        {
            isPasswordChar = value;
            if (!isPlaceHolder)
                textBox1.UseSystemPasswordChar = value;
        }
    }

    public int MaxCharacters
    {
        get => textBox1.MaxLength;
        set => textBox1.MaxLength = value;
    }

    public void SetAutoCompleteCustomSource(AutoCompleteStringCollection autoCompleteStringCollection)
    {
        textBox1.AutoCompleteCustomSource = autoCompleteStringCollection;
    }

    public void SetAutoCompleteMode(AutoCompleteMode autoCompleteMode)
    {
        textBox1.AutoCompleteMode = autoCompleteMode;
    }

    public void SetAutoCompleteSource(AutoCompleteSource autoCompleteSource)
    {
        textBox1.AutoCompleteSource = autoCompleteSource;
    }

    public bool Multiline
    {
        get => textBox1.Multiline;
        set => textBox1.Multiline = value;
    }

        
    public override Color BackColor
    {
        get => base.BackColor;
        set
        {
            base.BackColor = value;
            textBox1.BackColor = value;
        }
    }

        
    public override Color ForeColor
    {
        get => base.ForeColor;
        set
        {
            base.ForeColor = value;
            textBox1.ForeColor = value;
        }
    }

        
    public override Font Font
    {
        get => base.Font;
        set
        {
            base.Font = value;
            textBox1.Font = value;
            if (DesignMode)
            {
                UpdateControlHeight();
            }
        }
    }

        
    public override string Text
    {

        get => (isPlaceHolder || textBox1.Text == placeHolderText) ? string.Empty : textBox1.Text;
        set
        {
            if (value != null && value != placeHolderText && value.Length > 0 && value != "")
            {
                RemovePlaceholder();
            }
            else if (value != null && value.Length == 0 || value != "")
            {
                SetPlaceholder();
                textBox1.Text = placeHolderText;
            }
            textBox1.Text = value;
        }
    }

        
    public Color PlaceHolderColor
    {
        get => placeHolderColor;
        set
        {
            placeHolderColor = value;
            if (isPlaceHolder)
            {
                textBox1.ForeColor = value;
            }   
        }
    }

        
    public string PlaceHolderText
    {
        get => placeHolderText;
        set
        {
            placeHolderText = value;
            textBox1.Text = "";
            SetPlaceholder();
        }
    }

    public int SelectionStart
    {
        get => textBox1.SelectionStart;
        set => textBox1.SelectionStart = value;
    }

    public void Clear()
    {
        textBox1.Clear();
    }

    public RoundedTextBox()
    {
        InitializeComponent();
    }

    private void SetPlaceholder()
    {
        if (string.IsNullOrWhiteSpace(textBox1.Text) && placeHolderText != "")
        {
            Font = new Font(Font, FontStyle.Italic);
            isPlaceHolder = true;
            textBox1.Text = placeHolderText;
            textBox1.ForeColor = placeHolderColor;
            if (isPasswordChar)
                textBox1.UseSystemPasswordChar = false;
        }
    }
    private void RemovePlaceholder()
    {
        if (isPlaceHolder && placeHolderText != "")
        {
            Font = new Font(Font, FontStyle.Regular);
            isPlaceHolder = false;
            textBox1.Text = "";
            textBox1.ForeColor = ForeColor;
            if (isPasswordChar)
                textBox1.UseSystemPasswordChar = true;
        }
    }
    private GraphicsPath GetFigurePath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var curveSize = radius * 2F;

        path.StartFigure();
        path.AddArc(rect.X, rect.Y, curveSize, curveSize, 180, 90);
        path.AddArc(rect.Right - curveSize, rect.Y, curveSize, curveSize, 270, 90);
        path.AddArc(rect.Right - curveSize, rect.Bottom - curveSize, curveSize, curveSize, 0, 90);
        path.AddArc(rect.X, rect.Bottom - curveSize, curveSize, curveSize, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void UpdateControlHeight()
    {
        if (textBox1.Multiline == false)
        {
            var txtHeight = TextRenderer.MeasureText("Text", Font).Height + 1;
            textBox1.Multiline = true;
            textBox1.MinimumSize = new Size(0, txtHeight);
            textBox1.Multiline = false;

            Height = textBox1.Height + Padding.Top + Padding.Bottom;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (DesignMode)
            UpdateControlHeight();
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        UpdateControlHeight();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graph = e.Graphics;

        if (borderRadius > 1)
        {
            var rectBorderSmooth = ClientRectangle;
            var rectIcon = new Rectangle(ClientRectangle.X + 3, ClientRectangle.Y + (ClientRectangle.Height / 2) - (textBox1.Height / 2), textBox1.Height, textBox1.Height);

            var smoothSize = 2;


            using (var pathBorderSmooth = GetFigurePath(rectBorderSmooth, borderRadius))
            using (var penBorderSmooth = new Pen(Parent.BackColor, smoothSize))
            {

                Region = new Region(pathBorderSmooth);
                if (icon != null)
                {
                    graph.DrawImage(icon, rectIcon);
                }
                graph.SmoothingMode = SmoothingMode.AntiAlias;
                graph.DrawPath(penBorderSmooth, pathBorderSmooth);
            }
        }
        else 
        {
            Region = new Region(ClientRectangle);
        }
    }

    private void TextBox1_LostFocus(object sender, EventArgs e)
    {
        SetPlaceholder();
    }

    private void TextBox1_GotFocus(object sender, EventArgs e)
    {
        RemovePlaceholder();
    }

    private void TextBox1_MouseEnter(object sender, EventArgs e)
    {
        OnMouseEnter(e);
    }

    private void TextBox1_MouseLeave(object sender, EventArgs e)
    {
        OnMouseLeave(e);
    }

    private void TextBox1_KeyPress(object sender, KeyPressEventArgs e)
    {
        OnKeyPress(e);
    }

    private void TextBox1_Enter(object sender, EventArgs e)
    {
        OnEnter(e);
    }

    private void TextBox1_TextChanged(object sender, EventArgs e)
    {
        if (isPlaceHolder) return;
        OnTextChanged(e);
    }

    private void TextBox1_Click(object sender, EventArgs e)
    {
        OnClick(e);
    }
}