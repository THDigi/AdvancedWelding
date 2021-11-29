namespace Digi.AdvancedWelding
{
    public interface IUpdatable
    {
        void Update();
    }

    public abstract class ComponentBase
    {
        protected readonly AdvancedWeldingMod Main;

        public ComponentBase(AdvancedWeldingMod main)
        {
            Main = main;
            Main.Components.Add(this);
        }

        public abstract void Register();

        public abstract void Dispose();

        static protected void SetUpdate(IUpdatable obj, bool update)
        {
            if(update)
                AdvancedWeldingMod.Instance.UpdateObjects.Add(obj);
            else
                AdvancedWeldingMod.Instance.UpdateObjects.Remove(obj);
        }
    }
}
