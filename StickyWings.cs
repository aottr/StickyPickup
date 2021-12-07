
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;

public class StickyWings : UdonSharpBehaviour
{
    public VRCObjectPool ObjectPool;

    public override void Interact()
    {
        var obj = ObjectPool.TryToSpawn();
        obj.transform.position = Networking.LocalPlayer.GetBonePosition(HumanBodyBones.Chest) - new Vector3(0,0,0.3f);
        obj.transform.rotation = Networking.LocalPlayer.GetRotation();
    }
}
