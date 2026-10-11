namespace BlueDrop
{
    public sealed class TransferStore
    {
        private readonly object _sync = new();
        private TransferItem? _outgoing;
        private TransferItem? _incoming;
        private bool _incomingPending;

        public event Action? Changed;

        public event Action<TransferItem>? IncomingReceived;

        public TransferItem? Outgoing
        {
            get
            {
                lock (_sync)
                {
                    return _outgoing;
                }
            }
            set
            {
                lock (_sync)
                {
                    _outgoing = value;
                }

                Changed?.Invoke();
            }
        }

        public TransferItem? Incoming
        {
            get
            {
                lock (_sync)
                {
                    return _incoming;
                }
            }
        }

        public bool IncomingPending
        {
            get
            {
                lock (_sync)
                {
                    return _incomingPending;
                }
            }
        }

        public void Receive(TransferItem item)
        {
            lock (_sync)
            {
                _incoming = item;
                _incomingPending = true;
            }

            Changed?.Invoke();
            IncomingReceived?.Invoke(item);
        }

        public void ClearOutgoing(TransferItem item)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(_outgoing, item))
                {
                    return;
                }

                _outgoing = null;
            }

            Changed?.Invoke();
        }

        public void MarkIncomingTaken()
        {
            lock (_sync)
            {
                _incomingPending = false;
            }

            Changed?.Invoke();
        }
    }
}
