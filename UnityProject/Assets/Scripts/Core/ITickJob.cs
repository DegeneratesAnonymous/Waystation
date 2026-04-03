// ITickJob — interface for systems that can run on worker threads via Unity Job System.
namespace Waystation.Core
{
    public interface ITickJob
    {
        /// <summary>Prepare blittable data for the job. Called on main thread.</summary>
        void Prepare();

        /// <summary>Execute the job. May run on a worker thread.</summary>
        void Execute(int index);

        /// <summary>Apply results back to managed state. Called on main thread after job completes.</summary>
        void Complete();
    }
}
