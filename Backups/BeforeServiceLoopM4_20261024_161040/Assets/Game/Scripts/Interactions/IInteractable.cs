namespace MotelGame.Interactions
{
    public interface IInteractable
    {
        bool CanInteract { get; }
        void Interact();
    }
}
