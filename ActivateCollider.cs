using System.Collections;
using UnityEngine;

public class ActivateCollider : MonoBehaviour
{
    public float timeToActivate = 5.0f;
    public GameObject objectToActivate;
    public bool activateOnlyOnce = false;

    private bool hasActivated = false;

    void OnTriggerEnter(Collider other)
    {
        if (!hasActivated || !activateOnlyOnce)
        {
            if (timeToActivate > 0)
            {
                StartCoroutine(ActivateObjectAfterTime());
            }
        }
    }

    IEnumerator ActivateObjectAfterTime()
    {
        yield return new WaitForSeconds(timeToActivate);
        objectToActivate.SetActive(true);
        if (activateOnlyOnce)
        {
            hasActivated = true;
        }
    }
}
