namespace MotelGame.Interactions
{
    public interface IInteractable
    {
        bool CanInteract { get; }
        string Prompt { get; }
        void Interact();
    }
}
