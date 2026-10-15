//using UnityEngine;

//namespace MyUdonHelpers.Helpers
//{
//    [System.Serializable]
//    public class UdonListUnoCard 
//    {
//        public UnoCard[] items;
//        public int count;

//        // Constructor with optional initial size
//        public UdonListUnoCard(int initialSize = 108)
//        {
//            if (initialSize <= 0) initialSize = 1;
//            items = new UnoCard[initialSize];
//            count = 0;
//        }

//        // Add item, automatically resizes if full
//        public bool Add(UnoCard card)
//        {
//            if (count >= items.Length)
//                Resize(items.Length * 2);
//            items[count++] = card;
//            return true;
//        }

//        // Remove item at index
//        public bool RemoveAt(int index)
//        {
//            if (index < 0 || index >= count)
//                return false;

//            for (int i = index; i < count - 1; i++)
//                items[i] = items[i + 1];

//            items[count - 1] = default(UnoCard);
//            count--;
//            return true;
//        }

//        // Get item at index
//        public UnoCard Get(int index)
//        {
//            if (index < 0 || index >= count)
//                return default(UnoCard);
//            return items[index];
//        }

//        // Clear all items
//        public void Clear()
//        {
//            for (int i = 0; i < count; i++)
//                items[i] = default(UnoCard);
//            count = 0;
//        }

//        // Get current number of elements
//        public int Size()
//        {
//            return count;
//        }

//        // Shuffle the list (Fisher-Yates)
//        public void Shuffle()
//        {
//            for (int i = count - 1; i > 0; i--)
//            {
//                int j = Random.Range(0, i + 1);
//                UnoCard temp = items[i];
//                items[i] = items[j];
//                items[j] = temp;
//            }
//        }

//        // Internal array resize
//        private void Resize(int newSize)
//        {
//            UnoCard[] newArray = new UnoCard[newSize];
//            for (int i = 0; i < count; i++)
//                newArray[i] = items[i];
//            items = newArray;
//        }
//    }
//}
