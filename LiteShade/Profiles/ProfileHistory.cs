using System.Collections.Generic;
using LiteShade.Configuration;

namespace LiteShade.Profiles;

internal sealed class ProfileHistory(ColorProfile profile)
{
    private readonly List<ColorProfile> _states = [profile.Copy()];
    private int _position;

    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _states.Count - 1;

    public void Record(ColorProfile current)
    {
        if (current == _states[_position])
        {
            return;
        }

        _states.RemoveRange(_position + 1, _states.Count - _position - 1);
        _states.Add(current.Copy());
        if (_states.Count > 65)
        {
            _states.RemoveAt(0);
        }

        _position = _states.Count - 1;
    }

    public ColorProfile Undo() => (CanUndo ? _states[--_position] : _states[_position]).Copy();
    public ColorProfile Redo() => (CanRedo ? _states[++_position] : _states[_position]).Copy();
}
