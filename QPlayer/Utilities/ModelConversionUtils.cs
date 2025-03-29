using NAudio.Wave;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.ViewModels;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Utilities;

public static class ModelConversionUtils
{


    /*public static void SyncModel<TTo, TFrom>(TTo modelTo, TFrom modelFrom)
    {
        base.ToModel(cue);
        if (cue is VideoCue scue)
        {
            scue.path = MainViewModel?.ResolvePath(MainViewModel.ResolvePath(Path), false) ?? Path;
            scue.startTime = StartTime;
            scue.duration = PlaybackDuration;
            scue.volume = Volume;
            scue.fadeIn = FadeIn;
            scue.fadeOut = FadeOut;
            scue.fadeType = FadeType;
        }
    }

    internal static class ReflectionCacheExpression<TModelSrc, TModelDst> 
        where TModelSrc : class 
        where TModelDst : class
    {
        private static bool compiled = false;
        private static MarshalStruct marshalStructFunc = null;
        private static UnMarshalStruct unmarshalStructFunc = null;

        public static bool Supported => RuntimeFeature.IsDynamicCodeSupported;
        public static bool Compiled => compiled;
        public static MarshalStruct MarshalStructFunc => marshalStructFunc;
        public static UnMarshalStruct UnmarshalStructFunc => unmarshalStructFunc;

        internal delegate TModelDst MarshalStruct(TModelSrc obj);
        internal delegate TModelSrc UnMarshalStruct(TModelDst obj);

        public static void Compile(IDictionary<string, Action<>>)
        {
            var localType = typeof(TModelDst);
            var nativeType = typeof(TModelSrc);

            var mobj = Expression.Parameter(nativeType, "inVal");
            var uobj = Expression.Parameter(localType, "inVal");
            var mret = Expression.Variable(localType, "mret");
            var mretInit = Expression.Assign(mret, Expression.New(localType));
            var uret = Expression.Variable(nativeType, "uret");
            var uretInit = Expression.Assign(uret, Expression.New(nativeType));

            var mblocks = new List<Expression>();
            var ublocks = new List<Expression>();

            var memInst = Expression.Constant(mem);

            mblocks.Add(mretInit);
            ublocks.Add(uretInit);

            FieldInfo[] nativeFields = nativeType.GetFields();
            for (int i = 0; i < nativeFields.Length; i++)
            {
                FieldInfo nativeFld = nativeFields[i];
#if DEBUG
                try
                {
#endif
                    // Match fields by name, setting the destination fields to the corresponding source fields
                    var localFld = localType.GetField(nativeFld.Name);
                    // Load the field to convert onto the stack
                    Expression mvalSrc = Expression.Field(mobj, nativeFld);
                    Expression uvalSrc = Expression.Field(uobj, localFld);
                    var mvalDst = Expression.Field(mret, localFld);
                    var uvalDst = Expression.Field(uret, nativeFld);

                    

                    // Now that the field has been marshalled, write it back to the destination object
                    //try
                    //{
                    var massgn = Expression.Assign(mvalDst, mvalSrc);
                    var uassgn = Expression.Assign(uvalDst, uvalSrc);
                    mblocks.Add(massgn);
                    ublocks.Add(uassgn);
#if DEBUG
                }
                catch (Exception ex)
                {
                    throw new FieldAccessException($"Failed to marshal field '{nativeFld.Name}' in '{nativeFld.ReflectedType.FullName}'. \n" +
                        $"Failed with internal exception:\n", ex);
                }
#endif
            }

            //mblocks.Add(Expression.Call(null, typeof(Debugger).GetMethod(nameof(Debugger.Break))));
            mblocks.Add(mret);
            ublocks.Add(uret);
            var mblock = Expression.Block(localType, new[] { mret }, mblocks);
            var ublock = Expression.Block(nativeType, new[] { uret }, ublocks);

            var mfunc = Expression.Lambda<MarshalStruct>(mblock, mobj);
            var ufunc = Expression.Lambda<UnMarshalStruct>(ublock, uobj);

            marshalStructFunc = mfunc.Compile();
            unmarshalStructFunc = ufunc.Compile();

            compiled = true;
        }
    }*/
}
