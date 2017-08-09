using System;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;

namespace UnityEditor.VFX
{
    sealed class VFXBuiltInExpression : VFXExpression
    {
        private static readonly VFXExpression TotalTime = new VFXBuiltInExpression(VFXExpressionOp.kVFXTotalTimeOp, VFXValueType.kFloat);
        private static readonly VFXExpression DeltaTime = new VFXBuiltInExpression(VFXExpressionOp.kVFXDeltaTimeOp, VFXValueType.kFloat);
        private static readonly VFXExpression SystemSeed = new VFXBuiltInExpression(VFXExpressionOp.kVFXSystemSeedOp, VFXValueType.kUint);

        private static readonly VFXExpression[] AllExpressions = VFXReflectionHelper.CollectStaticReadOnlyExpression<VFXExpression>(typeof(VFXBuiltInExpression));
        public static readonly VFXExpressionOp[] All = AllExpressions.Select(e => e.Operation).ToArray();

        public static VFXExpression Find(VFXExpressionOp op)
        {
            var expression = AllExpressions.FirstOrDefault(e => e.Operation == op);
            if (expression == null)
            {
                Debug.LogErrorFormat("Unable to find BuiltIn Parameter from op : {0}", op);
            }
            return expression;
        }

        private VFXExpressionOp m_Operation;
        private VFXValueType m_ValueType;

        private VFXBuiltInExpression(VFXExpressionOp op, VFXValueType valueType)
            : base(Flags.None)
        {
            m_Operation = op;
            m_ValueType = valueType;
        }

        public sealed override VFXExpressionOp Operation
        {
            get
            {
                return m_Operation;
            }
        }

        public sealed override VFXValueType ValueType
        {
            get
            {
                return m_ValueType;
            }
        }

        public override bool Equals(object obj)
        {
            if (!(obj is VFXBuiltInExpression))
                return false;

            var other = (VFXBuiltInExpression)obj;
            return ValueType == other.ValueType && Operation == other.Operation;
        }

        public override int GetHashCode()
        {
            return Operation.GetHashCode();
        }

        protected sealed override VFXExpression Evaluate(VFXExpression[] constParents)
        {
            return this;
        }
    }
}
