using System;
using System.Collections.Generic;
using System.Text;

namespace ErdcServerMcp.Dto
{
    internal class TypeTreeNodeVo
    {
        /// <summary>
        /// 对象唯一标识（对应 JSON 中 "oid"）
        /// </summary>
        public string Oid { get; set; }

        /// <summary>
        /// 节点 Id（对应 JSON 中 "id"）
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 显示名称（对应 JSON 中 "displayName"）
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 模型类（对应 JSON 中 "modelClass"）
        /// </summary>
        public string ModelClass { get; set; }

        /// <summary>
        /// 类型名称（对应 JSON 中 "typeName"）
        /// </summary>
        public string TypeName { get; set; }


        public string TableName { get; set; }
        /// <summary>
        /// 子节点集合
        /// </summary>
        public List<TypeTreeNodeVo> Children { get; set; } = new List<TypeTreeNodeVo>();
    }
}
