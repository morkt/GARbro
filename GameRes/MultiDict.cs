//! \file       GameRes.cs
//! \date       Mon Jun 30 20:12:13 2014
//! \brief      game resources browser.
//
// copy-pasted from some post on the stackoverflow.
//

using System;
using System.Collections.Generic;

namespace GameRes.Collections
{
    /// <summary>
    /// Extension to the normal Dictionary. This class can store more than one value for every key.
    /// It keeps a HashSet for every Key value.  Calling Add with the same Key and multiple values
    /// will store each value under the same Key in the Dictionary. Obtaining the values for a Key
    /// will return the HashSet with the Values of the Key. 
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    public class MultiValueDictionary<TKey, TValue> : Dictionary<TKey, HashSet<TValue>> //, IEnumerable<KeyValuePair<TKey, TValue>>, System.Collections.IEnumerable
    {
    	/// <summary>
    	/// Initializes a new instance of the <see cref="MultiValueDictionary&lt;TKey, TValue&gt;"/> class.
    	/// </summary>
    	public MultiValueDictionary() : base()
    	{
    	}

    	/// <summary>
    	/// Adds the specified value under the specified key
    	/// </summary>
    	/// <param name="key">The key.</param>
    	/// <param name="value">The value.</param>
    	public void Add(TKey key, TValue value)
    	{
    		HashSet<TValue> container = null;
    		if(!this.TryGetValue(key, out container))
    		{
    			container = new HashSet<TValue>();
    			base.Add(key, container);
    		}
    		container.Add(value);
    	}

    	/// <summary>
    	/// Removes the specified value for the specified key. It will leave the key in the dictionary.
    	/// </summary>
    	/// <param name="key">The key.</param>
    	/// <param name="value">The value.</param>
    	public void Remove(TKey key, TValue value)
    	{
    		HashSet<TValue> container = null;
    		if(this.TryGetValue(key, out container))
    		{
    			container.Remove(value);
    			if(container.Count <= 0)
    			{
    				this.Remove(key);
    			}
    		}
    	}

    	/// <summary>
        /// Gets the values for the key specified. This method is useful if you want to avoid an
        /// exception for key value retrieval and you can't use TryGetValue (e.g. in lambdas)
    	/// </summary>
    	/// <param name="key">The key.</param>
        /// <param name="returnEmptySet">if set to true and the key isn't found, an empty hashset is
        /// returned, otherwise, if the key isn't found, null is returned</param>
    	/// <returns>
        /// This method will return null (or an empty set if returnEmptySet is true) if the key
        /// wasn't found, or the values if key was found.
    	/// </returns>
    	public HashSet<TValue> GetValues(TKey key, bool returnEmptySet)
    	{
    		HashSet<TValue> toReturn = null;
    		if (!base.TryGetValue(key, out toReturn) && returnEmptySet)
    		{
    			toReturn = new HashSet<TValue>();
    		}
    		return toReturn;
    	}

        /*
        // hides Dictionary.GetEnumerator()
        new public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            Enumerator e = new Enumerator();
            e.key_enumerator = base.GetEnumerator();
            e.current_pair = new KeyValuePair<TKey, TValue>();
            if (e.key_enumerator.MoveNext())
                e.value_enumerator = e.key_enumerator.Current.Value.GetEnumerator();
            else
                e.value_enumerator = null;
            return e;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return (System.Collections.IEnumerator)GetEnumerator();
        }

        [SerializableAttribute]
        new public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDisposable, System.Collections.IEnumerator
        {
            public Dictionary<TKey, HashSet<TValue>>.Enumerator key_enumerator;
            public HashSet<TValue>.Enumerator? value_enumerator;
            public KeyValuePair<TKey, TValue> current_pair;

            public KeyValuePair<TKey, TValue> Current { get { return current_pair; } }
            object System.Collections.IEnumerator.Current { get { return Current; } }

            void IDisposable.Dispose() { }
            
            void System.Collections.IEnumerator.Reset()
            {
            }
            
            private void SetCurrent ()
            {
                current_pair = new KeyValuePair<TKey, TValue>(key_enumerator.Current.Key, value_enumerator.Value.Current);
                Console.WriteLine("Enumerator.SetCurrent ({0} => {1})", current_pair.Key, current_pair.Value);
            }

            private void ResetCurrent ()
            {
                current_pair = new KeyValuePair<TKey, TValue>(default(TKey), default(TValue));
            }

            public bool MoveNext ()
            {
                if (null == value_enumerator)
                {
                    ResetCurrent();
                    return false;
                }
                if (value_enumerator.Value.MoveNext())
                {
                    SetCurrent();
                    return true;
                }
                if (!key_enumerator.MoveNext())
                {
                    value_enumerator = null;
                    ResetCurrent();
                    return false;
                }
                value_enumerator = key_enumerator.Current.Value.GetEnumerator();
                if (value_enumerator.Value.MoveNext())
                {
                    SetCurrent();
                    return true;
                }
                else
                {
                    current_pair = new KeyValuePair<TKey, TValue>(key_enumerator.Current.Key, default(TValue));
                    return false;
                }
            }
        }
        */
    }
}
