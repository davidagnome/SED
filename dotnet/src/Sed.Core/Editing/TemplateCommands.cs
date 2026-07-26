using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>Adds or changes one parameter of a template; an empty value removes it.</summary>
public sealed class SetTemplateValueCommand : IEditCommand
{
    private readonly Template _template;
    private readonly string _key;
    private readonly string? _new;
    private readonly string? _old;

    public SetTemplateValueCommand(Template template, string key, string? value)
    {
        _template = template;
        _key = key;
        _new = string.IsNullOrWhiteSpace(value) ? null : value;
        _old = template.Values.TryGetValue(key, out var existing) ? existing : null;
    }

    public string Name => _new is null ? $"Remove {_key}" : $"Set {_key}";

    public void Apply() => Write(_new);
    public void Revert() => Write(_old);

    private void Write(string? value)
    {
        if (value is null) _template.Values.Remove(_key);
        else _template.Values[_key] = value;
    }
}

/// <summary>Changes a template's parent (the name it inherits parameters from).</summary>
public sealed class SetTemplateParentCommand : IEditCommand
{
    private readonly Template _template;
    private readonly string _new, _old;

    public SetTemplateParentCommand(Template template, string parent)
    {
        _template = template;
        _new = parent;
        _old = template.Parent;
    }

    public string Name => "Set template parent";
    public void Apply() => _template.Parent = _new;
    public void Revert() => _template.Parent = _old;
}

/// <summary>Adds a template to the level, appended after the existing ones.</summary>
public sealed class CreateTemplateCommand : IEditCommand
{
    private readonly Level _level;

    public Template Template { get; }

    public CreateTemplateCommand(Level level, Template template)
    {
        _level = level;
        Template = template;
    }

    public string Name => $"Create template {Template.Name}";

    public void Apply()
    {
        Template.Order = _level.Templates.Count == 0
            ? 0
            : _level.Templates.Values.Max(t => t.Order) + 1;
        _level.Templates[Template.Name] = Template;
    }

    public void Revert() => _level.Templates.Remove(Template.Name);
}

/// <summary>
/// Removes a template. Things and child templates that referenced it keep their
/// (now dangling) name rather than being silently rewritten — the consistency of
/// those references is the author's call, and undo restores the template anyway.
/// </summary>
public sealed class DeleteTemplateCommand : IEditCommand
{
    private readonly Level _level;
    private readonly Template _template;

    public DeleteTemplateCommand(Level level, Template template)
    {
        _level = level;
        _template = template;
    }

    public string Name => $"Delete template {_template.Name}";

    public void Apply() => _level.Templates.Remove(_template.Name);
    public void Revert() => _level.Templates[_template.Name] = _template;

    /// <summary>Things that instantiate this template — worth warning about first.</summary>
    public static int CountUsers(Level level, string templateName) =>
        level.Things.Count(t => string.Equals(t.Template, templateName, StringComparison.OrdinalIgnoreCase))
        + level.Templates.Values.Count(t => string.Equals(t.Parent, templateName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Renames a template and repoints everything that referred to it — things that
/// instantiate it and child templates that inherit from it. Leaving those behind
/// would silently break the level, so they move with the rename and come back on
/// undo.
/// </summary>
public sealed class RenameTemplateCommand : IEditCommand
{
    private readonly Level _level;
    private readonly Template _template;
    private readonly string _newName;
    private readonly string _oldName;

    private readonly List<Thing> _things = new();
    private readonly List<Template> _children = new();

    public RenameTemplateCommand(Level level, Template template, string newName)
    {
        _level = level;
        _template = template;
        _newName = newName;
        _oldName = template.Name;
    }

    public string Name => $"Rename template to {_newName}";

    /// <summary>Null when the rename is allowed, else why not.</summary>
    public static string? Validate(Level level, Template template, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return "A template needs a name.";
        if (newName.Any(char.IsWhiteSpace)) return "Template names cannot contain spaces.";
        if (string.Equals(newName, template.Name, StringComparison.OrdinalIgnoreCase)) return null;
        if (level.Templates.ContainsKey(newName)) return $"A template called '{newName}' already exists.";
        return null;
    }

    public void Apply()
    {
        _things.Clear();
        _children.Clear();

        foreach (var thing in _level.Things)
            if (string.Equals(thing.Template, _oldName, StringComparison.OrdinalIgnoreCase))
                _things.Add(thing);

        foreach (var tpl in _level.Templates.Values)
            if (!ReferenceEquals(tpl, _template) &&
                string.Equals(tpl.Parent, _oldName, StringComparison.OrdinalIgnoreCase))
                _children.Add(tpl);

        Rewrite(_newName);
    }

    public void Revert() => Rewrite(_oldName);

    private void Rewrite(string name)
    {
        _level.Templates.Remove(_template.Name);
        _template.Name = name;
        _level.Templates[name] = _template;

        foreach (var thing in _things) thing.Template = name;
        foreach (var child in _children) child.Parent = name;
    }
}
