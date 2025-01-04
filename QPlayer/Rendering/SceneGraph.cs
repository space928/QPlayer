using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QPlayer.Rendering;

public class SceneGraph
{
    public readonly Dictionary<int, Material> materialCache = [];
    //public Camera camera = new();

    public ReadOnlyCollection<SceneObject> SceneObjects => sceneObjectsRO;

    private readonly List<SceneObject> sceneObjects = [];
    private ReadOnlyCollection<SceneObject> sceneObjectsRO;

    public SceneGraph()
    {
        sceneObjectsRO = sceneObjects.AsReadOnly();
    }

    public void Clear()
    {
        sceneObjects.Clear();
    }

    public void SetParent(SceneObject sceneObj, SceneObject? parent)
    {
        if (sceneObj.Parent == parent)
            return;

        if (sceneObj.Parent == null)
        {
            sceneObjects.Remove(sceneObj);
            parent!.children ??= [];
            parent.children.Add(sceneObj);
            parent.OnChildrenChanged(sceneObj, true);
            sceneObj.Parent = parent;
        }
        else
        {
            sceneObj.Parent.RemoveChild(sceneObj);
            if (parent != null)
            {
                parent.children ??= [];
                parent.children.Add(sceneObj);
                parent.OnChildrenChanged(sceneObj, true);
            }
            else
            {
                sceneObjects.Add(sceneObj);
            }
            sceneObj.Parent = parent;
        }
    }

    public void Remove(SceneObject? sceneObj)
    {
        if (sceneObj == null)
            return;

        if (!sceneObjects.Remove(sceneObj))
        {
            foreach (var root in sceneObjects)
            {
                if (root.RemoveChild(sceneObj))
                    break;
            }
        }
    }

    public void Add(SceneObject sceneObj, SceneObject? parent = null)
    {
        if (sceneObj.Parent == null && parent == null)
            sceneObjects.Add(sceneObj);

        if (parent != null)
        {
            parent.children ??= [];
            parent.children.Add(sceneObj);
            parent.OnChildrenChanged(sceneObj, true);
            sceneObj.Parent = parent;
        }
    }

    // Ugly method...
    internal void Swap(int first, int second)
    {
        if (first < 0 || first >= sceneObjects.Count || second < 0 || second >= sceneObjects.Count)
            return;
        //throw new ArgumentOutOfRangeException();

        (sceneObjects[first], sceneObjects[second]) = (sceneObjects[second], sceneObjects[first]);
    }
}

public class SceneObject
{
    public Transform transform = new();
    public string name = "SceneObj";
    private SceneObject? parent;
    public List<SceneObject>? children;
    protected Bounds objBounds;

    public Bounds Bounds => objBounds;

    public SceneObject? Parent
    {
        get => parent;
        internal set
        {
            // TODO: Having this setter here ignores many checks that SceneGraph would normally do which could break the scene.
            parent = value;
            transform.Parent = value?.transform;
        }
    }

    public SceneObject()
    {
        transform.TransformChanged += _ => UpdateBounds();
    }

    public SceneObject(SceneObject other) : this()
    {
        transform = new(other.transform);
        name = other.name + " - Clone";
        parent = other.Parent;
        children = null;//other.children;
        objBounds = other.objBounds;
    }

    public SceneObject(Transform transform, string name, List<SceneObject>? children, Bounds bounds) : this()
    {
        this.transform = transform;
        this.name = name;
        this.children = children;
        objBounds = bounds;
        if (children != null)
        {
            //UpdateBounds();
            foreach (var child in children)
                child.Parent = this;
        }
    }

    public virtual SceneObject Clone()
    {
        return new(this);
    }

    internal void OnChildrenChanged(SceneObject? child, bool added)
    {
        if (added)
        {
            if (child != null)
                objBounds = objBounds.Union(child.Bounds);
        }
        else
        {
            UpdateBounds();
        }
    }

    public void UpdateBounds()
    {
        if (children != null && children.Count > 0)
            objBounds = children.Select(x => x.Bounds)
                                .Aggregate((last, next) => last.Union(next));
        else
            objBounds = new(transform.Pos, System.Numerics.Vector3.One * 0.5f);
    }

    internal bool RemoveChild(SceneObject sceneObj)
    {
        if (children == null)
            return false;

        if (children.Remove(sceneObj))
        {
            OnChildrenChanged(sceneObj, false);
            return true;
        }

        foreach (var child in children)
            if (child.RemoveChild(sceneObj))
            {
                OnChildrenChanged(sceneObj, false);
                return true;
            }

        return false;
    }
}
