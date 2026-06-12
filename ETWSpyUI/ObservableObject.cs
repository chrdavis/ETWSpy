using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ETWSpyUI
{
    /// <summary>
    /// Lightweight base class that implements <see cref="INotifyPropertyChanged"/> so that
    /// data-bound entries refresh in the UI when their properties change in place.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Sets the backing field and raises <see cref="PropertyChanged"/> when the value changes.
        /// </summary>
        /// <returns><c>true</c> if the value changed; otherwise <c>false</c>.</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
