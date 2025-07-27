using System.Numerics;
using Karpik.Engine.Client;
using Karpik.Engine.Client.VisualElements;
using Karpik.Engine.Shared;

namespace Karpik.Game.Modules;

public class PauseMenu : VisualElement
{
    private readonly Button _resumeButton;
    private readonly Label _title;
    private readonly InputField _inputField;

    public PauseMenu(Vector2 size) : base(size)
    {
        _resumeButton = new Button(new Vector2(600, 200), "Resume");
        _resumeButton.Clicked += OnResumeClicked;
        _resumeButton.Anchor = Anchor.Center;
        
        _title = new Label("My game");
        _title.OffsetPosition = new Vector2(0, 60);
        _title.Anchor = Anchor.TopCenter;
        _title.Stretch = StretchMode.Horizontal;

        _inputField = new InputField(new Vector2(600, 200));
        _inputField.OffsetPosition = new Vector2(0, 260);
        _inputField.Anchor = Anchor.Center;
        _inputField.Stretch = StretchMode.Horizontal;
        
        Add(_resumeButton);
        Add(_title);
        Add(_inputField);
    }

    private void OnResumeClicked()
    {
        Time.IsPaused = false;
        Close();
    }
    
    public void Open()
    {
        IsVisible = IsEnabled = true;
    }

    public void Close()
    {
        IsVisible = IsEnabled = false;
    }
}