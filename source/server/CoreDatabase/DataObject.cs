using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using DOL.Database.Attributes;
using DOL.Database.UniqueID;

namespace DOL.Database
{
    public abstract class DataObject : ICloneable, IEquatable<DataObject>
    {
        private DataObject _snapshot;
        private bool _allowAdd = true;
        private bool _allowDelete = true;
        private string _objectId;
        private int? _cachedHash;

        public virtual bool UsesPreCaching => AttributeUtil.GetPreCachedFlag(GetType());

        [Browsable(false)]
        public virtual string TableName => AttributeUtil.GetTableName(GetType());

        [Browsable(false)]
        public bool IsPersisted { get; set; }

        [Browsable(false)]
        public virtual bool AllowAdd
        {
            get => _allowAdd;
            set => _allowAdd = value;
        }

        [Browsable(false)]
        public virtual bool AllowDelete
        {
            get => _allowDelete;
            set => _allowDelete = value;
        }

        [Browsable(false)]
        public string ObjectId
        {
            get => _objectId;
            set
            {
                _objectId = value;
                _cachedHash = null; // Just in case. ObjectId should never be changed after creation.
            }
        }

        [Browsable(false)]
        public virtual bool Dirty { get; set; }

        [Browsable(false)]
        public virtual bool IsDeleted { get; set; }

        /// <summary>
        /// Port of OpenDAoC-Core 19123fffd: only stamped when the row is about to be written with a real change
        /// (GetDirtyBindings) or inserted. Before, a Dirty object always read "now" here, so every flagged object (every
        /// item of a loaded inventory, every slot move) differed from its snapshot and was written even when nothing
        /// changed.
        /// </summary>
        [DataElement(AllowDbNull = false, Index = false)]
        public DateTime LastTimeRowUpdated { get; set; }

        protected DataObject()
        {
            ObjectId = IdGenerator.GenerateID();
            IsPersisted = false;
            AllowAdd = true;
            AllowDelete = true;
            IsDeleted = false;
        }

        public void TakeSnapshot()
        {
            // Called when an object as been created and its properties initialized.
            // Creates a copy of itself to be able to keep track of dirty properties.
            _snapshot = (DataObject) MemberwiseClone();
            _snapshot.Dirty = false;
            // The clone copied this object's previous snapshot, so every save kept the one before it and each saved
            // object held all its past versions (6,000 bots: 41,293 bot records 20 minutes in; heap dump 2026-10-15).
            _snapshot._snapshot = null;
        }

        /// <summary>Remember the exact state written by a detached save, without
        /// clearing later actor mutations or copying saved values over them.</summary>
        public void AcceptPersistenceBaseline(DataObject saved)
        {
            if (saved == null || saved.GetType() != GetType() || saved.ObjectId != ObjectId)
                throw new ArgumentException("A persistence baseline must describe the same object.", nameof(saved));
            if (!saved.IsPersisted || saved.Dirty || saved.IsDeleted) return;
            _snapshot = saved._snapshot ?? (DataObject)saved.MemberwiseClone();
            _snapshot._snapshot = null;
            // The detached copy was stamped when it was written; only the save path sets this column, so carrying it
            // over cannot hide an actor change, and the next flush does not rewrite the row for the stamp alone.
            LastTimeRowUpdated = _snapshot.LastTimeRowUpdated;
        }

        public List<ElementBinding> GetDirtyBindings(DataTableHandler tableHandler)
        {
            // If there's no snapshot, we can't know what changed.
            if (_snapshot == null)
            {
                LastTimeRowUpdated = DateTime.UtcNow;
                return tableHandler.UpdateElementBindings.ToList();
            }

            List<ElementBinding> dirtyBindings = new();

            foreach (ElementBinding binding in tableHandler.UpdateElementBindings)
            {
                if (ReferenceEquals(binding, tableHandler.LastUpdatedBinding))
                    continue;

                object currentValue = binding.GetValue(this);
                object originalValue = binding.GetValue(_snapshot);

                if (!Equals(currentValue, originalValue))
                    dirtyBindings.Add(binding);
            }

            if (dirtyBindings.Count > 0 && tableHandler.LastUpdatedBinding != null)
            {
                LastTimeRowUpdated = DateTime.UtcNow;
                dirtyBindings.Add(tableHandler.LastUpdatedBinding);
            }

            return dirtyBindings;
        }

        public object Clone()
        {
            var obj = (DataObject) MemberwiseClone();
            obj._snapshot = null;
            obj.IsPersisted = false;
            obj.ObjectId = IdGenerator.GenerateID();
            return obj;
        }

        public override string ToString()
        {
            return $"DataObject: {TableName}, ObjectId{{{ObjectId}}}";
        }

        public override int GetHashCode()
        {
            if (_cachedHash.HasValue)
                return _cachedHash.Value;

            _cachedHash = ObjectId.GetHashCode();
            return _cachedHash.Value;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DataObject);
        }

        public bool Equals(DataObject other)
        {
            if (other is null)
                return false;

            return ReferenceEquals(this, other) || ObjectId == other.ObjectId;
        }
    }
}
