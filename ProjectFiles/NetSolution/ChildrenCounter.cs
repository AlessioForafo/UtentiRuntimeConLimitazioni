#region Using directives
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.OPCUAServer;
using System.Collections.Generic;
using System.Linq;
using UAManagedCore;
#endregion

public class ChildrenCounter : BaseNetLogic
{
    /// <summary>
    /// This method initializes a new instance of the ChildrenCounter class.
    /// It sets up the logic object and initializes the references observer.
    /// </summary>
    public override void Start()
    {
        ListaUtenti();

        contatore = LogicObject.GetVariable("Count");
        contatore.VariableChange += Contatore_VariableChange;

        var nodePointerVariable = LogicObject.GetVariable("Node");
        if (nodePointerVariable == null)
        {
            Log.Error("ChildrenCounter", "Missing Node variable on ChildrenCounter");
            return;
        }

        var nodePointerValue = (NodeId)nodePointerVariable.Value;
        if (nodePointerValue == null || nodePointerValue == NodeId.Empty)
        {
            Log.Warning("ChildrenCounter", "Node variable not set");
            return;
        }

        var countVariable = LogicObject.GetVariable("Count");
        if (countVariable == null)
        {
            Log.Error("ChildrenCounter", "Missing variable Count on ChildrenCounter");
            return;
        }

        var resolvedResult = InformationModel.Get(nodePointerValue);
        countVariable.Value = resolvedResult.Children.Count;

        if (referencesEventRegistration != null)
        {
            referencesEventRegistration.Dispose();
            referencesEventRegistration = null;
        }

        referencesObserver = new ReferencesObserver(resolvedResult, countVariable);
        referencesObserver.Initialize();

        referencesEventRegistration = resolvedResult.RegisterEventObserver(
            referencesObserver, EventType.ForwardReferenceAdded | EventType.ForwardReferenceRemoved);
    }

    private void Contatore_VariableChange(object sender, VariableChangeEventArgs e)
    {
        ListaUtenti();
    }

    public override void Stop()
    {
        referencesEventRegistration?.Dispose();

        referencesEventRegistration = null;
        referencesObserver = null;
        contatore.VariableChange -= Contatore_VariableChange;
    }
    
    [ExportMethod]
    public void ListaUtenti()
    {
        var ListaUtenti = LogicObject.GetVariable("ListaUtenti");
        ListaUtenti.Value = "";

        //creazione dizionario per gestire gruppi in maniera gerarchica
        Dictionary<string, int> groupLevels = new()
        {
            { "Operator", 1 },
            { "Maintanance", 2 },
            { "Supervisor", 3 },
            { "Administrator", 4 }
        };

        //recupero la lista dei gruppi a cui appartiene l'utente collegato
        //Se un utente appartiene a più gruppi, considero il più alto:
        var loggedUser = Session.User;
        var loggedUserGroups = loggedUser.Refs.GetObjects(FTOptix.Core.ReferenceTypes.HasGroup, false);

        if (loggedUserGroups == null || !loggedUserGroups.Any())
        {
            Log.Warning("Security", "L'utente non appartiene ad alcun gruppo");
            ListaUtenti.Value = $"SELECT * FROM Model WHERE BrowseName = ' '";
            return;
        }
        int loggedUserLevel = loggedUserGroups.Select(g => groupLevels[g.BrowseName]).Max();

        //confronto i gruppi degli utenti del progetto con quelli dell'utente collegato
        var utentiFiltro = new List<string>();
        var projectUsers = InformationModel.Get(LogicObject.GetVariable("Node").Value);
        foreach (var user in projectUsers.Children)
        {
            var userGroups = user.Refs.GetObjects(FTOptix.Core.ReferenceTypes.HasGroup, false);
            if (userGroups != null && userGroups.Any())
            {
                int userLevel = userGroups.Select(g => groupLevels[g.BrowseName]).Max();

                if (userLevel <= loggedUserLevel)
                {
                    // Visualizza l'utente
                    utentiFiltro.Add(user.BrowseName);
                }
            }

        }
        if (utentiFiltro.Count > 0)
        {
            ListaUtenti.Value = $"SELECT * FROM Model WHERE BrowseName IN ('{string.Join("','", utentiFiltro)}')";
        }
        else
        {
            ListaUtenti.Value = $"SELECT * FROM Model WHERE BrowseName = ' '";
        }
    }


    private class ReferencesObserver : IReferenceObserver
    {
        /// <summary>
        /// This method initializes a new instance of the ReferencesObserver class with the specified node to monitor and a count variable.
        /// </summary>
        /// <param name="nodeToMonitor">The node to monitor for changes.</param>
        /// <param name="countVariable">The variable to track the count of references.</param>
        /// <returns>
        /// A new instance of ReferencesObserver with the specified node and count variable initialized.
        /// </returns>
        public ReferencesObserver(IUANode nodeToMonitor, IUAVariable countVariable)
        {
            this.nodeToMonitor = nodeToMonitor;
            this.countVariable = countVariable;
        }

        /// <summary>
        /// This method sets the value of <see cref="countVariable"/> to the count of children in <see cref="nodeToMonitor"/>.
        /// </summary>
        /// <remarks>
        /// This method is used to initialize the count of monitored nodes.
        /// </remarks>
        public void Initialize()
        {
            countVariable.Value = nodeToMonitor.Children.Count;
        }

        /// <summary>
        /// This method increments the count variable when a reference is added between two nodes.
        /// </summary>
        /// <param name="sourceNode">The source node in the reference.</param>
        /// <param name="targetNode">The target node in the reference.</param>
        /// <param name="referenceTypeId">The type of reference being added.</param>
        /// <param name="senderId">The identifier of the sender node.</param>
        public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
        {
            if (IsReferenceAllowed(referenceTypeId))
            {
                ++countVariable.Value;
            }
        }

        /// <summary>
        /// This method handles the removal of a reference, checking if the reference type is allowed
        /// and decrementing a counter if the counter is greater than zero.

        /// </summary>
        public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
        {
            if (IsReferenceAllowed(referenceTypeId) && countVariable.Value > 0)
            {
                --countVariable.Value;                
            }
        }



        /// <summary>
        /// This method checks if a given reference type is allowed to be used.
        /// It returns true if the reference type is either 
        /// <see cref="UAManagedCore.OpcUa.ReferenceTypes.HasComponent"/> or 
        /// <see cref="UAManagedCore.OpcUa.ReferenceTypes.HasOrderedComponent"/>.
        /// </summary>
        /// <param name="referenceTypeId">The type identifier of the reference to check.</param>
        /// <returns>
        /// A boolean value indicating whether the reference type is allowed.
        /// </returns>
        public bool IsReferenceAllowed(NodeId referenceTypeId)
        {
            return referenceTypeId == UAManagedCore.OpcUa.ReferenceTypes.HasComponent ||
                   referenceTypeId == UAManagedCore.OpcUa.ReferenceTypes.HasOrderedComponent;
        }

        private readonly IUANode nodeToMonitor;
        private readonly IUAVariable countVariable;
    }

    private ReferencesObserver referencesObserver;
    private IEventRegistration referencesEventRegistration;
    private IUAVariable contatore;
}
