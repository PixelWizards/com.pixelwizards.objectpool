using UnityEngine;

namespace PixelWizards.ObjectPool
{
    public class ReturnInstanceTimer : MonoBehaviour
    {
        public float origTimer = 3.0f;
        public float timer = 2.0f;

        /// <summary>
        /// reset ourself on enable
        /// </summary>
        private void OnEnable()
        {
            timer = origTimer;
        }

        public void SetTimer(float thisTime)
        {
            origTimer = thisTime;
            timer = origTimer;
        }

        //private void OnDestroy()
        //{
        //    Debug.Log("ReturnInstanceTimer() - was destroyed!");
        //}

        /// <summary>
        /// start the countdown and reset 
        /// </summary>
        public void Update()
        {
            timer -= Time.deltaTime;

            if (timer < 0f)
            {
                timer = origTimer;
                PoolManager.ReturnInstance(gameObject);
            }
        }
    }
}