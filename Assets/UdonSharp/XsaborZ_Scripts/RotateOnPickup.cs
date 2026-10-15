
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class RotateOnPickup : UdonSharpBehaviour
{
    #region Description
    [Header("When Held Rotate")]
    [TextArea(1, 20)] // min 1 lines, max 20 lines
    [Tooltip("You can write notes here that appear in the Inspector.")]
    public string Description = "Write your notes here";
    #endregion
    #region Variables
    private Transform modelToRotate;
    bool isHeld; // if picked up rotate Vector3Up 90 degrees
    private BoxCollider parentCollider;
    private BoxCollider childCollider;

    public float rotationSpeed = 90f; // degrees per second
    #endregion
    void Start()
    {
        isHeld = false;
        modelToRotate = transform.GetChild(0);
        parentCollider = GetComponentInChildren<BoxCollider>();
        childCollider = modelToRotate.GetComponent<BoxCollider>();
        //mTransform = modelToRotate.GetComponent<Transform>();
        childCollider.enabled = false;
    }
    public override void OnPickup()
    {
        isHeld = true;
        if (!parentCollider) return;
        else
            parentCollider.enabled = false;
        if (!childCollider) return;
        else
            childCollider.enabled = true;
    }
    public override void OnDrop()
    {
        isHeld = false;
        if (!parentCollider) return;
        else
            parentCollider.enabled = true;
        if (!childCollider) return;
        else
            childCollider.enabled = false;
        AdjustParentCollider();
    }
    private void Update() 
    {
        if (!isHeld) return;
        modelToRotate.Rotate(Vector3.up * rotationSpeed * Time.deltaTime, Space.Self);
    }
    private void AdjustParentCollider()
    {
        // used to fix transform of parent colliders when swapping from child to parent
        Vector3 childWorldPos = modelToRotate.position;
        Quaternion childWorldRot = modelToRotate.rotation;
        Transform parent = this.transform;
        parent.position = childWorldPos;
        parent.rotation = childWorldRot;
        modelToRotate.localPosition = Vector3.zero;
        modelToRotate.localRotation = Quaternion.identity;
    }
}