using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Experimental.VFX;
using UnityEngine.Experimental.UIElements;
using System.Reflection;

using NodeID = System.UInt32;

namespace UnityEditor.VFX.UI
{
    class VFXPaste : VFXCopyPasteCommon
    {
        public Dictionary<NodeID, VFXNodeController> newControllers;
        public Vector2 pasteOffset;
        public List<KeyValuePair<VFXContext, List<VFXBlock>>> newContexts;
        protected static VFXSlot FetchSlot(IVFXSlotContainer container, int[] slotPath, bool input)
        {
            int containerSlotIndex = slotPath[slotPath.Length - 1];

            VFXSlot slot = null;
            if (input)
            {
                if (container.GetNbInputSlots() > containerSlotIndex)
                {
                    slot = container.GetInputSlot(slotPath[slotPath.Length - 1]);
                }
            }
            else
            {
                if (container.GetNbOutputSlots() > containerSlotIndex)
                {
                    slot = container.GetOutputSlot(slotPath[slotPath.Length - 1]);
                }
            }
            if (slot == null)
            {
                return null;
            }

            for (int i = slotPath.Length - 2; i >= 0; --i)
            {
                if (slot.GetNbChildren() > slotPath[i])
                {
                    slot = slot[slotPath[i]];
                }
                else
                {
                    return null;
                }
            }

            return slot;
        }
        static VFXPaste s_Instance = null;

        public static void UnserializeAndPasteElements(VFXViewController viewController, Vector2 center, string data, VFXView view = null, VFXGroupNodeController groupNode = null)
        {
            var copyData = JsonUtility.FromJson<SerializableGraph>(data);

            if( s_Instance == null)
                s_Instance = new VFXPaste();
            s_Instance.PasteCopy(viewController, center, copyData, view, groupNode);
        }

        public void PasteCopy(VFXViewController viewController, Vector2 center, object data, VFXView view, VFXGroupNodeController groupNode)
        {
            SerializableGraph copyData = (SerializableGraph)data;

            if (copyData.blocksOnly)
            {
                if (view != null)
                {
                    PasteBlocks(view, copyData);
                }
            }
            else
            {
                PasteAll(viewController, center, copyData, view, groupNode);
            }
        }

        static readonly GUIContent m_BlockPasteError = EditorGUIUtility.TextContent("To paste blocks, please select one target block or one target context.");

        void PasteBlocks(VFXView view, SerializableGraph copyData)
        {
            var selectedContexts = view.selection.OfType<VFXContextUI>();
            var selectedBlocks = view.selection.OfType<VFXBlockUI>();

            VFXBlockUI targetBlock = null;
            VFXContextUI targetContext = null;

            if (selectedBlocks.Count() > 0)
            {
                targetBlock = selectedBlocks.OrderByDescending(t => t.context.controller.model.GetIndex(t.controller.model)).First();
                targetContext = targetBlock.context;
            }
            else if (selectedContexts.Count() == 1)
            {
                targetContext = selectedContexts.First();
            }
            else
            {
                Debug.LogError(m_BlockPasteError.text);
                return;
            }

            VFXContext targetModelContext = targetContext.controller.model;

            int targetIndex = -1;
            if (targetBlock != null)
            {
                targetIndex = targetModelContext.GetIndex(targetBlock.controller.model) + 1;
            }

            var newBlocks = new HashSet<VFXBlock>();

            newControllers = new Dictionary<NodeID, VFXNodeController>();

            foreach (var block in copyData.operatorsOrBlocks)
            {
                Node blk = block;
                VFXBlock newBlock = PasteAndInitializeNode<VFXBlock>(view.controller, ref blk);

                if (targetModelContext.AcceptChild(newBlock, targetIndex))
                {
                    newBlocks.Add(newBlock);
                    targetModelContext.AddChild(newBlock, targetIndex, false); // only notify once after all blocks have been added

                    targetIndex++;
                }
            }

            targetModelContext.Invalidate(VFXModel.InvalidationCause.kStructureChanged);

            //TODO fill infos.indexToController for when external links will optionally copied.

            view.ClearSelection();

            foreach (var uiBlock in targetContext.Query().OfType<VFXBlockUI>().Where(t => newBlocks.Contains(t.controller.model)).ToList())
            {
                view.AddToSelection(uiBlock);
            }
        }


        void PasteDataEdges(ref SerializableGraph copyData)
        {
            if (copyData.dataEdges != null)
            {
                foreach (var dataEdge in copyData.dataEdges)
                {
                    if (dataEdge.input.targetIndex == InvalidID || dataEdge.output.targetIndex == InvalidID)
                        continue;

                    //TODO: This bypasses viewController.CreateLink, and all its additional checks it shouldn't.
                    VFXModel inputModel = newControllers.ContainsKey(dataEdge.input.targetIndex) ? newControllers[dataEdge.input.targetIndex].model:null;

                    VFXNodeController outputController = newControllers.ContainsKey(dataEdge.output.targetIndex) ? newControllers[dataEdge.output.targetIndex] : null;
                    VFXModel outputModel = outputController != null ? outputController.model : null;
                    if( inputModel != null && outputModel != null)
                    {
                        VFXSlot outputSlot = FetchSlot(outputModel as IVFXSlotContainer, dataEdge.output.slotPath, false);
                        VFXSlot inputSlot = FetchSlot(inputModel as IVFXSlotContainer, dataEdge.input.slotPath, true);

                        inputSlot.Link(outputSlot);

                        if (outputController is VFXParameterNodeController)
                        {
                            var parameterNodeController = outputController as VFXParameterNodeController;

                            parameterNodeController.infos.linkedSlots.Add(new VFXParameter.NodeLinkedSlot { inputSlot = inputSlot, outputSlot = outputSlot });
                        }
                    }
                }
            }
        }
        VFXContext PasteContext(VFXViewController controller,ref Context context)
        {
            VFXContext newContext = PasteAndInitializeNode<VFXContext>(controller, ref context.node);

            if (newContext == null)
            {
                newContexts.Add(new KeyValuePair<VFXContext, List<VFXBlock>>(null, null));
                return null;
            }


            List<VFXBlock> blocks = new List<VFXBlock>();
            foreach(var block in context.blocks)
            {
                var blk = block;

                VFXBlock newBlock = PasteAndInitializeNode<VFXBlock>(null,ref blk);

                newBlock.enabled = (blk.flags & Node.Flags.Enabled) == Node.Flags.Enabled;

                blocks.Add(newBlock);

                if ( newBlock != null)
                    newContext.AddChild(newBlock);
            }
            newContexts.Add(new KeyValuePair<VFXContext, List<VFXBlock>>(newContext, blocks));

            return newContext;
        }

        T PasteAndInitializeNode<T>(VFXViewController controller, ref Node node) where T : VFXModel
        {
            Type type = node.type;
            if (type == null)
                return null;
            var newNode = ScriptableObject.CreateInstance(type) as T;
            if (newNode == null)
                return null;

            var ope = node;
            PasteNode(newNode, ref ope);

            if( ! (newNode is VFXBlock))
                controller.graph.AddChild(newNode);

            return newNode;
        }

        void PasteModelSettings(VFXModel model,Property[] settings,Type type)
        {
            var fields = GetFields(type);

            for(int i = 0 ; i < settings.Length ; ++i)
            {
                string name = settings[i].name;
                var field = fields.Find(t=>t.Name == name);
                if(field != null)
                    field.SetValue(model,settings[i].value.Get());
            }
        }

        void PasteNode(VFXModel model,ref Node node)
        {
            model.position = node.position + pasteOffset;

            PasteModelSettings(model,node.settings,model.GetType());

            model.Invalidate(VFXModel.InvalidationCause.kSettingChanged);

            var slotContainer = model as IVFXSlotContainer;
            var inputSlots = slotContainer.inputSlots;
            for(int i = 0 ; i < node.inputSlots.Length ; ++i)
            {
                if( inputSlots[i].name == node.inputSlots[i].name )
                {
                    inputSlots[i].value = node.inputSlots[i].value.Get();
                }
            }

            foreach (var slot in AllSlots(slotContainer.inputSlots))
            {
                slot.collapsed = !node.expandedInputs.Contains(slot.path);
            }
            foreach (var slot in AllSlots(slotContainer.outputSlots))
            {
                slot.collapsed = !node.expandedOutputs.Contains(slot.path);
            }

        }

        void PasteAll(VFXViewController viewController, Vector2 center, SerializableGraph copyData, VFXView view, VFXGroupNodeController groupNode)
        {
            newControllers = new Dictionary<NodeID, VFXNodeController>();

            var graph = viewController.graph;
            pasteOffset = (copyData.bounds.width > 0 && copyData.bounds.height > 0) ? center - copyData.bounds.center : Vector2.zero;

            // look if pasting there will result in the first element beeing exactly on top of other
            while (true)
            {
                bool foundSamePosition = false;
                if (copyData.contexts != null && copyData.contexts.Length > 0)
                {
                    var type = copyData.contexts[0].node.type;

                    foreach (var existingContext in viewController.graph.children.OfType<VFXContext>())
                    {
                        if ((copyData.contexts[0].node.position + pasteOffset - existingContext.position).sqrMagnitude < 1)
                        {
                            foundSamePosition = true;
                            break;
                        }
                    }
                }
                else if (copyData.operatorsOrBlocks != null && copyData.operatorsOrBlocks.Length > 0)
                {
                    foreach (var existingSlotContainer in viewController.graph.children.Where(t => t is IVFXSlotContainer))
                    {
                        if ((copyData.operatorsOrBlocks[0].position + pasteOffset - existingSlotContainer.position).sqrMagnitude < 1)
                        {
                            foundSamePosition = true;
                            break;
                        }
                    }
                }
                else if (copyData.parameters != null && copyData.parameters.Length > 0 && copyData.parameters[0].nodes.Length > 0)
                {
                    foreach (var existingSlotContainer in viewController.graph.children.Where(t => t is IVFXSlotContainer))
                    {
                        if ((copyData.parameters[0].nodes[0].position + pasteOffset - existingSlotContainer.position).sqrMagnitude < 1)
                        {
                            foundSamePosition = true;
                            break;
                        }
                    }
                }
                else if (copyData.stickyNotes != null && copyData.stickyNotes.Length > 0)
                {
                    foreach (var stickyNote in viewController.stickyNotes)
                    {
                        if ((copyData.stickyNotes[0].position.position + pasteOffset - stickyNote.position.position).sqrMagnitude < 1)
                        {
                            foundSamePosition = true;
                            break;
                        }
                    }
                }
                else if (copyData.groupNodes != null && copyData.groupNodes.Length > 0)
                {
                    foreach (var gn in viewController.groupNodes)
                    {
                        if ((copyData.groupNodes[0].infos.position.position + pasteOffset - gn.position.position).sqrMagnitude < 1)
                        {
                            foundSamePosition = true;
                            break;
                        }
                    }
                }

                if (foundSamePosition)
                {
                    pasteOffset += Vector2.one * 30;
                }
                else
                {
                    break;
                }
            }

            PasteContexts(viewController, ref copyData);

            List<VFXOperator> newOperators = PasteOperators(viewController, ref copyData);

            List<KeyValuePair<VFXParameter, List<int>>> newParameters = PasteParameters(viewController, ref copyData);

            // Create controllers for all new nodes
            viewController.LightApplyChanges();

            for (int i = 0; i < newContexts.Count; ++i)
            {
                if (newContexts[i].Key != null)
                {
                    VFXContextController controller = viewController.GetNodeController(newContexts[i].Key, 0) as VFXContextController;
                    newControllers[ContextFlag | (uint)i] = controller;

                    for (int j = 0; j < newContexts[i].Value.Count; ++j)
                    {
                        var block = newContexts[i].Value[j];
                        if (block != null)
                        {
                            VFXBlockController blockController = controller.blockControllers.First(t => t.model == block);
                            if (blockController != null)
                                newControllers[GetBlockID((uint)i, (uint)j)] = blockController;
                        }
                    }
                }
            }
            for (int i = 0; i < newOperators.Count; ++i)
            {
                newControllers[OperatorFlag | (uint)i] = viewController.GetNodeController(newOperators[i], 0);
            }
            for (int i = 0; i < newParameters.Count; ++i)
            {
                viewController.GetParameterController(newParameters[i].Key).ApplyChanges();

                for (int j = 0; j < newParameters[i].Value.Count; j++)
                {
                    var nodeController = viewController.GetNodeController(newParameters[i].Key, newParameters[i].Value[j]) as VFXParameterNodeController;
                    newControllers[GetParameterNodeID((uint)i, (uint)j)] = nodeController;
                }
            }

            int firstCopiedGroup = -1;
            int firstCopiedStickyNote = -1;
            VFXUI ui = viewController.graph.UIInfos;
            firstCopiedStickyNote = ui.stickyNoteInfos != null ? ui.stickyNoteInfos.Length : 0;

            if (copyData.groupNodes != null && copyData.groupNodes.Length > 0)
            {
                if (ui.groupInfos == null)
                {
                    ui.groupInfos = new VFXUI.GroupInfo[0];
                }
                firstCopiedGroup = ui.groupInfos.Length;

                List<VFXUI.GroupInfo> newGroupInfos = new List<VFXUI.GroupInfo>();
                foreach (var groupInfos in copyData.groupNodes)
                {
                    var newGroupInfo = new VFXUI.GroupInfo();
                    newGroupInfo.position = new Rect(groupInfos.infos.position.position + pasteOffset, groupInfos.infos.position.size);
                    newGroupInfo.title = groupInfos.infos.title;
                    newGroupInfos.Add(newGroupInfo);
                    newGroupInfo.contents = groupInfos.contents.Take(groupInfos.contents.Length - groupInfos.stickNodeCount).Select(t => { VFXNodeController node = null; newControllers.TryGetValue(t, out node); return node; }).Where(t => t != null).Select(node => new VFXNodeID(node.model, node.id))
                                            .Concat(groupInfos.contents.Skip(groupInfos.contents.Length - groupInfos.stickNodeCount).Select(t => new VFXNodeID((int)t + firstCopiedStickyNote)))
                                            .ToArray();

                }
                ui.groupInfos = ui.groupInfos.Concat(newGroupInfos).ToArray();
            }
            if (copyData.stickyNotes != null && copyData.stickyNotes.Length > 0)
            {
                if (ui.stickyNoteInfos == null)
                {
                    ui.stickyNoteInfos = new VFXUI.StickyNoteInfo[0];
                }
                ui.stickyNoteInfos = ui.stickyNoteInfos.Concat(copyData.stickyNotes.Select(t => new VFXUI.StickyNoteInfo(t) { position = new Rect(t.position.position + pasteOffset, t.position.size) })).ToArray();
            }

            PasteDataEdges(ref copyData);

            if (copyData.flowEdges != null)
            {
                foreach (var flowEdge in copyData.flowEdges)
                {
                    VFXContext inputContext = newControllers.ContainsKey(flowEdge.input.contextIndex) ? (newControllers[flowEdge.input.contextIndex] as VFXContextController).model : null;
                    VFXContext outputContext = newControllers.ContainsKey(flowEdge.output.contextIndex) ? (newControllers[flowEdge.output.contextIndex] as VFXContextController).model : null;

                    if (inputContext != null && outputContext != null)
                        inputContext.LinkFrom(outputContext, flowEdge.input.flowIndex, flowEdge.output.flowIndex);
                }
            }
            
            for (int i = 0; i < newContexts.Count; ++i)
            {
                VFXNodeController nodeController = null;
                newControllers.TryGetValue(ContextFlag | (uint)i, out nodeController);
                var contextController = nodeController as VFXContextController;

                if ( contextController != null)
                {
                    if( (contextController.flowInputAnchors.Count() == 0 || 
                    contextController.flowInputAnchors.First().connections.Count() == 0 || 
                    contextController.flowInputAnchors.First().connections.First().output.context.model.GetData() == null ) &&
                    copyData.contexts[i].dataIndex >= 0)
                    {
                        var data = copyData.datas[copyData.contexts[i].dataIndex];
                        VFXData targetData = contextController.model.GetData();
                        if( targetData != null)
                        {
                            PasteModelSettings(targetData, data.settings, targetData.GetType());
                        }
                    }
                }
            }

            // Create all ui based on model
            viewController.LightApplyChanges();

            if (view != null)
            {
                view.ClearSelection();

                var elements = view.graphElements.ToList();

                List<VFXNodeUI> newSlotContainerUIs = new List<VFXNodeUI>();
                List<VFXContextUI> newContextUIs = new List<VFXContextUI>();

                foreach (var slotContainer in newContexts.Select(t => t.Key).OfType<VFXContext>())
                {
                    VFXContextUI contextUI = elements.OfType<VFXContextUI>().FirstOrDefault(t => t.controller.model == slotContainer);
                    if (contextUI != null)
                    {
                        newSlotContainerUIs.Add(contextUI);
                        newSlotContainerUIs.AddRange(contextUI.GetAllBlocks().Cast<VFXNodeUI>());
                        newContextUIs.Add(contextUI);
                        view.AddToSelection(contextUI);
                    }
                }
                foreach (var slotContainer in newControllers.OfType<VFXOperatorController>())
                {
                    VFXOperatorUI slotContainerUI = elements.OfType<VFXOperatorUI>().FirstOrDefault(t => t.controller == slotContainer);
                    if (slotContainerUI != null)
                    {
                        newSlotContainerUIs.Add(slotContainerUI);
                        view.AddToSelection(slotContainerUI);
                    }
                }

                foreach (var param in newControllers.OfType<VFXParameterNodeController>())
                {
                    foreach (var parameterUI in elements.OfType<VFXParameterUI>().Where(t => t.controller == param))
                    {
                        newSlotContainerUIs.Add(parameterUI);
                        view.AddToSelection(parameterUI);
                    }
                }

                // Simply selected all data edge with the context or slot container, they can be no other than the copied ones
                foreach (var dataEdge in elements.OfType<VFXDataEdge>())
                {
                    if (newSlotContainerUIs.Contains(dataEdge.input.GetFirstAncestorOfType<VFXNodeUI>()))
                    {
                        view.AddToSelection(dataEdge);
                    }
                }
                // Simply selected all data edge with the context or slot container, they can be no other than the copied ones
                foreach (var flowEdge in elements.OfType<VFXFlowEdge>())
                {
                    if (newContextUIs.Contains(flowEdge.input.GetFirstAncestorOfType<VFXContextUI>()))
                    {
                        view.AddToSelection(flowEdge);
                    }
                }

                if (groupNode != null)
                {
                    foreach (var newSlotContainerUI in newSlotContainerUIs)
                    {
                        groupNode.AddNode(newSlotContainerUI.controller);
                    }
                }

                //Select all groups that are new
                if (firstCopiedGroup >= 0)
                {
                    foreach (var gn in elements.OfType<VFXGroupNode>())
                    {
                        if (gn.controller.index >= firstCopiedGroup)
                        {
                            view.AddToSelection(gn);
                        }
                    }
                }

                //Select all groups that are new
                if (firstCopiedStickyNote >= 0)
                {
                    foreach (var gn in elements.OfType<VFXStickyNote>())
                    {
                        if (gn.controller.index >= firstCopiedStickyNote)
                        {
                            view.AddToSelection(gn);
                        }
                    }
                }
            }
        }

        private void PasteContexts(VFXViewController viewController, ref SerializableGraph copyData)
        {
            if (copyData.contexts != null)
            {
                newContexts = new List<KeyValuePair<VFXContext, List<VFXBlock>>>();
                foreach (var context in copyData.contexts)
                {
                    var ctx = context;
                    PasteContext(viewController, ref ctx);
                }
            }
        }

        private List<VFXOperator> PasteOperators(VFXViewController viewController, ref SerializableGraph copyData)
        {
            List<VFXOperator> newOperators = new List<VFXOperator>();
            if (copyData.operatorsOrBlocks != null)
            {
                foreach (var operat in copyData.operatorsOrBlocks)
                {
                    Node ope = operat;
                    VFXOperator newOperator = PasteAndInitializeNode<VFXOperator>(viewController, ref ope);

                    newOperators.Add(newOperator); // add even they are null so that the index is correct
                }
            }

            return newOperators;
        }

        private List<KeyValuePair<VFXParameter, List<int>>> PasteParameters(VFXViewController viewController, ref SerializableGraph copyData)
        {
            List<KeyValuePair<VFXParameter, List<int>>> newParameters = new List<KeyValuePair<VFXParameter, List<int>>>();

            if (copyData.parameters != null)
            {
                foreach (var parameter in copyData.parameters)
                {
                    // if we have a parameter with the same name use it else create it with the copied data
                    VFXParameter p = viewController.graph.children.OfType<VFXParameter>().FirstOrDefault(t => t.GetInstanceID() == parameter.originalInstanceID);

                    if (p == null)
                    {
                        Type type = parameter.value.type;
                        VFXModelDescriptorParameters desc = VFXLibrary.GetParameters().FirstOrDefault(t => t.model.type == type);
                        if (desc != null)
                        {
                            p = viewController.AddVFXParameter(Vector2.zero, desc);
                            p.value = parameter.value.Get();
                            p.hasRange = parameter.range;
                            if (parameter.range)
                            {
                                p.m_Min = parameter.min;
                                p.m_Max = parameter.max;
                            }
                            p.SetSettingValue("m_exposedName", parameter.name); // the controller will take care or name unicity later
                            p.tooltip = parameter.tooltip;
                        }

                    }

                    if (p == null)
                    {
                        newParameters.Add(new KeyValuePair<VFXParameter, List<int>>(null, null));
                        continue;
                    }


                    var newParameterNodes = new List<int>();
                    foreach (var node in parameter.nodes)
                    {
                        int nodeIndex = p.AddNode(node.position + pasteOffset);

                        var nodeModel = p.nodes.LastOrDefault(t => t.id == nodeIndex);
                        nodeModel.expanded = !node.collapsed;
                        nodeModel.expandedSlots = AllSlots(p.outputSlots).Where(t => node.expandedOutput.Contains(t.path)).ToList();

                        newParameterNodes.Add(nodeIndex);
                    }

                    newParameters.Add(new KeyValuePair<VFXParameter, List<int>>(p, newParameterNodes));
                }
            }

            return newParameters;
        }
    }
}
