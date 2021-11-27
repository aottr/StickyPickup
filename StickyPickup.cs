/*
 * Copyright 2021 AlexOttr <alex@ottr.one>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, 
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to 
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF 
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY 
 * CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace OttrOne.StickyPickup
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StickyPickup : UdonSharpBehaviour
    {
        [Header("StickyPickup v1.0.0")]
        public HumanBodyBones Bone;
        public bool OnlyVR;
        public float Radius = 0.3f;
        private bool stickedOn = false;

        private Vector3 trackedPos;
        private Quaternion trackedRot;

        private bool isKinematic;

        private Rigidbody rigidBody;
        public void Start()
        {
            this.rigidBody = (Rigidbody)gameObject.GetComponent(typeof(Rigidbody));
            this.isKinematic = rigidBody.isKinematic;
        }

        public override void OnPickup()
        {
            stickedOn = false;
            rigidBody.isKinematic = true;
        }
        public override void OnDrop()
        {
            Vector3 objPos = this.rigidBody.transform.position;
            Vector3 plyPos = Networking.LocalPlayer.GetBonePosition(Bone);
            Quaternion invRot = Quaternion.Inverse(Networking.LocalPlayer.GetBoneRotation(Bone));

            // calculate 3d distance
            float offset = Mathf.Sqrt(Mathf.Pow(objPos.x - plyPos.x, 2) + Mathf.Pow(objPos.y - plyPos.y, 2) + Mathf.Pow(objPos.z - plyPos.z, 2));
            if (offset <= Radius) stickedOn = true;
            else
            {
                stickedOn = false;
                // resets kinematic on drop in world to re-enable gravity effect
                rigidBody.isKinematic = this.isKinematic;
            }

            // q^(-1) * Vector (x2-x1, y2-y1, z2-z1)
            trackedPos = invRot * (objPos - plyPos);
            // calculate the rotation by multiplying the current rotation with inverse player rotation
            trackedRot = invRot * this.rigidBody.transform.rotation;
        }

        public void Update()
        {
            if (stickedOn && (Networking.LocalPlayer.IsUserInVR() || !OnlyVR))
            {
                Vector3 bonePosition = Networking.LocalPlayer.GetBonePosition(Bone);
                Quaternion boneRotation = Networking.LocalPlayer.GetBoneRotation(Bone);

                // calculate the wanted offset by multiplying the bonerotation with the position offset and add this to the targeted bone position
                // calculate the wanted rotation by multiplying the current bone rotation with the calculated rotation offset 
                this.rigidBody.transform.SetPositionAndRotation((boneRotation * trackedPos) + bonePosition, boneRotation * trackedRot);
            }
        }
    }
}