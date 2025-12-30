#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mieruka.App.Ui;

/// <summary>
/// Custom double-buffered picture box control for high-performance preview rendering.
/// Avoids overhead and flicker of standard PictureBox during high frame rate updates.
/// </summary>
internal sealed class DoubleBufferedPictureBox : Control
{
    private Image? _image;

    public DoubleBufferedPictureBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;
        UpdateStyles();
        BackColor = Color.Black;
    }

    public Image? Image
    {
        get => _image;
        set
        {
            if (_image != value)
            {
                _image = value;
                Invalidate();
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_image is null)
        {
            return;
        }

        try
        {
            // Calculate scaling to fit image within control bounds while maintaining aspect ratio
            var imageRatio = (float)_image.Width / _image.Height;
            var controlRatio = (float)Width / Height;

            int drawWidth, drawHeight, drawX, drawY;

            if (imageRatio > controlRatio)
            {
                // Image is wider than control - fit to width
                drawWidth = Width;
                drawHeight = (int)(Width / imageRatio);
                drawX = 0;
                drawY = (Height - drawHeight) / 2;
            }
            else
            {
                // Image is taller than control - fit to height
                drawWidth = (int)(Height * imageRatio);
                drawHeight = Height;
                drawX = (Width - drawWidth) / 2;
                drawY = 0;
            }

            e.Graphics.DrawImage(_image, drawX, drawY, drawWidth, drawHeight);
        }
        catch
        {
            // Ignore drawing errors (e.g., if image is disposed during paint)
        }
    }
}
