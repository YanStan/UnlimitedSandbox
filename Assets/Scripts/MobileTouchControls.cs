using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Creates a touch UI at runtime. No scene Canvas or prefabs required.
public sealed class MobileTouchControls : MonoBehaviour
{
    public Vector2 Move { get; private set; }
    public bool Running { get; private set; }
    private bool jumpQueued;
    private Vector2 lookDelta;
    private RectTransform joystickKnob;
    private RectTransform joystickBase;
    private float joystickRadius = 65f;

    public Vector2 ConsumeLookDelta()
    {
        Vector2 result = lookDelta;
        lookDelta = Vector2.zero;
        return result;
    }

    public bool ConsumeJump()
    {
        bool result = jumpQueued;
        jumpQueued = false;
        return result;
    }

    public static MobileTouchControls Create()
    {
        var root = new GameObject("MobileTouchControls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MobileTouchControls));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("MobileEventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            events.AddComponent<StandaloneInputModule>();
#endif
        }
        var ui = root.GetComponent<MobileTouchControls>();
        ui.Build(root.transform);
        return ui;
    }

    private static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 size, Vector2 offset, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    private static void Label(RectTransform parent, string label)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var text = go.GetComponent<Text>();
        text.text = label;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 34;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
    }

    private void Build(Transform root)
    {
        // Full-screen look surface is created first, behind the joystick and buttons.
        var look = Panel(root, "LookArea", new Vector2(0.75f, 0.5f), new Vector2(960f, 1080f), Vector2.zero, new Color(0, 0, 0, 0));
        var lookHandler = look.gameObject.AddComponent<TouchArea>();
        lookHandler.owner = this;
        lookHandler.kind = TouchArea.Kind.Look;

        joystickBase = Panel(root, "JoystickBase", new Vector2(0f, 0f), new Vector2(210f, 210f), new Vector2(170f, 165f), new Color(0f, 0f, 0f, 0.38f));
        joystickKnob = Panel(joystickBase, "JoystickKnob", new Vector2(0.5f, 0.5f), new Vector2(100f, 100f), Vector2.zero, new Color(1f, 1f, 1f, 0.55f));
        joystickKnob.GetComponent<Image>().raycastTarget = false;
        var stick = joystickBase.gameObject.AddComponent<TouchArea>();
        stick.owner = this;
        stick.kind = TouchArea.Kind.Joystick;

        var run = Panel(root, "RunButton", new Vector2(0f, 0f), new Vector2(130f, 130f), new Vector2(360f, 155f), new Color(0f, 0f, 0f, 0.45f));
        Label(run, "RUN");
        var runHandler = run.gameObject.AddComponent<TouchArea>();
        runHandler.owner = this;
        runHandler.kind = TouchArea.Kind.Run;

        var jump = Panel(root, "JumpButton", new Vector2(1f, 0f), new Vector2(155f, 155f), new Vector2(-155f, 160f), new Color(0f, 0f, 0f, 0.45f));
        Label(jump, "JUMP");
        var jumpHandler = jump.gameObject.AddComponent<TouchArea>();
        jumpHandler.owner = this;
        jumpHandler.kind = TouchArea.Kind.Jump;
    }

    internal void SetStick(Vector2 screenPosition, Camera eventCamera)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(joystickBase, screenPosition, eventCamera, out Vector2 local))
        {
            Vector2 clamped = Vector2.ClampMagnitude(local, joystickRadius);
            joystickKnob.anchoredPosition = clamped;
            Move = clamped / joystickRadius;
        }
    }
    internal void ResetStick()
    {
        Move = Vector2.zero;
        joystickKnob.anchoredPosition = Vector2.zero;
    }
    internal void AddLook(Vector2 delta) { lookDelta += delta; }
    internal void SetRun(bool value) { Running = value; }
    internal void Jump() { jumpQueued = true; }

    private sealed class TouchArea : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        internal enum Kind { Look, Joystick, Run, Jump }
        internal Kind kind;
        internal MobileTouchControls owner;
        private int activePointer = int.MinValue;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (activePointer != int.MinValue) return;
            activePointer = eventData.pointerId;
            if (kind == Kind.Joystick) owner.SetStick(eventData.position, eventData.pressEventCamera);
            else if (kind == Kind.Run) owner.SetRun(true);
            else if (kind == Kind.Jump) owner.Jump();
        }
        public void OnDrag(PointerEventData eventData)
        {
            if (activePointer != eventData.pointerId) return;
            if (kind == Kind.Joystick) owner.SetStick(eventData.position, eventData.pressEventCamera);
            else if (kind == Kind.Look) owner.AddLook(eventData.delta);
        }
        public void OnPointerUp(PointerEventData eventData)
        {
            if (activePointer != eventData.pointerId) return;
            activePointer = int.MinValue;
            if (kind == Kind.Joystick) owner.ResetStick();
            else if (kind == Kind.Run) owner.SetRun(false);
        }
        private void OnDisable()
        {
            activePointer = int.MinValue;
            if (owner == null) return;
            if (kind == Kind.Joystick) owner.ResetStick();
            if (kind == Kind.Run) owner.SetRun(false);
        }
    }
}
