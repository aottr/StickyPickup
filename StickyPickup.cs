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
        [Header("StickyPickup v1.1.0 Beta")]
        public HumanBodyBones Bone;
        [Range(0.001f, 4f)]
        public float Radius = 0.3f;
        public bool OnlyVR;
        [Tooltip("Dropping the pickup inside the radius will attach it.")]
        public bool PlaceOnDrop = false;

        [Header("Reset Options")]
        [Tooltip("Trigger collider as a cage for the pickup.")]
        public Collider AreaCollider;
        [Tooltip("Reset height will be ignored if area collider is given.")]
        public float ResetHeight;

        private bool stickedOn = false;

        private Vector3 trackedPos;
        private Quaternion trackedRot;

        private bool isKinematic;

        private Vector3 _origPos;
        private Quaternion _origRot;

        private Rigidbody rigidBody;
        private VRC_Pickup pickup;
        public void Start()
        {
            this.rigidBody = (Rigidbody)gameObject.GetComponent(typeof(Rigidbody));
            this.pickup = (VRC_Pickup)gameObject.GetComponent(typeof(VRC_Pickup));
            this.isKinematic = rigidBody.isKinematic;

            if (!PlaceOnDrop) this.pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;

            _origPos = new Vector3(rigidBody.transform.position.x, rigidBody.transform.position.y, rigidBody.transform.position.z);
            _origRot = new Quaternion(rigidBody.transform.rotation.x, rigidBody.transform.rotation.y, rigidBody.transform.rotation.z, rigidBody.transform.rotation.w);
        }

        public override void OnPickup()
        {
            stickedOn = false;

            gameObject.SetActive(false);
            rigidBody.isKinematic = true;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Resets the position, bone and rotation of the item
        /// </summary>
        public void ResetPosition()
        {
            pickup.Drop();
            rigidBody.velocity = Vector3.zero;
            rigidBody.angularVelocity = Vector3.zero;
            this.gameObject.SetActive(false);
            rigidBody.isKinematic = this.isKinematic;
            this.gameObject.SetActive(true);
            this.rigidBody.transform.SetPositionAndRotation(_origPos, _origRot);
        }

        /// <summary>
        /// Drop calculates the current offset to the selected bone. If its within the radius it assumes that the player wants to place
        /// the item there so it attaches it to the player.
        /// 
        /// If its outside the radius it will put it into idle state.
        /// </summary>
        public override void OnDrop()
        {
            if (PlaceOnDrop)
            {
                Attach();
            }
            else
            {
                // resets kinematic on drop in world to re-enable gravity effect
                this.gameObject.SetActive(false);
                rigidBody.isKinematic = this.isKinematic;
                this.gameObject.SetActive(true);
            }
        }

        public override void OnPickupUseUp()
        {
            if (PlaceOnDrop) return;

            Attach();
        }

        private void Attach()
        {
            Vector3 objPos = this.rigidBody.transform.position;
            Vector3 plyPos = Networking.LocalPlayer.GetBonePosition(Bone);
            Quaternion invRot = Quaternion.Inverse(Networking.LocalPlayer.GetBoneRotation(Bone));

            if (Vector3.Distance(objPos, plyPos) <= Radius && (Networking.LocalPlayer.IsUserInVR() || !OnlyVR))
            {
                stickedOn = true;
            }
            else
            {
                stickedOn = false;
                // resets kinematic on drop in world to re-enable gravity effect
                this.gameObject.SetActive(false);
                rigidBody.isKinematic = this.isKinematic;
                this.gameObject.SetActive(true);
            }

            // q^(-1) * Vector (x2-x1, y2-y1, z2-z1)
            trackedPos = invRot * (objPos - plyPos);
            // calculate the rotation by multiplying the current rotation with inverse player rotation
            trackedRot = invRot * this.rigidBody.transform.rotation;
        }

        private void OnTriggerExit(Collider collider)
        {
            if (collider == AreaCollider)
            {
                ResetPosition();
            }
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
            if (AreaCollider == null && rigidBody.transform.position.y < ResetHeight) ResetPosition();
        }
    }
}