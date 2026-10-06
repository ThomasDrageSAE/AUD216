using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    [SerializeField] private AnimationEventHandler.AnnouncementType announcementType;

    public void PlayClearedSound()
    {
        AnimationEventHandler.Instance?.PlayClearedSound();
    }

    public void PlayDayNumberSound()
    {
        AnimationEventHandler.Instance?.PlayDayNumberSound();
    }

    public void OnAnimationComplete()
    {
        AnimationEventHandler.Instance?.OnAnimationComplete(announcementType);
    }
} 