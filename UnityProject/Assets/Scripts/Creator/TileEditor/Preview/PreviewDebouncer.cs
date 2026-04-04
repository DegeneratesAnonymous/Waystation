using System;

namespace Waystation.Creator.TileEditor.Preview
{
    public class PreviewDebouncer
    {
        private float _delay;
        private float _timer;
        private bool _pending;
        private Action _callback;

        public PreviewDebouncer(float delaySeconds = 0.1f)
        {
            _delay = delaySeconds;
        }

        public void Request(Action callback)
        {
            _callback = callback;
            _timer = _delay;
            _pending = true;
        }

        public void Update(float deltaTime)
        {
            if (!_pending) return;
            _timer -= deltaTime;
            if (_timer <= 0f)
            {
                _pending = false;
                _callback?.Invoke();
                _callback = null;
            }
        }

        public void Cancel()
        {
            _pending = false;
            _callback = null;
        }
    }
}
