﻿// ResearchTree/ResearchTree.cs
// 
// Copyright Karel Kroeze, 2015.
// 
// Created 2015-12-21 13:45

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityCoreLibrary.ColorPicker;
using UnityEngine;
using Verse;

namespace FluffyResearchTree
{
    public class ResearchTree
    {
        public static List<Node> Forest;
        public static List<Tree> Trees;
        public static Tree Orphans;
        public static IntVec2 OrphanDepths;
        public static int OrphanWidth;
        public static Texture2D Button = ContentFinder<Texture2D>.Get( "button" );
        public static Texture2D ButtonActive = ContentFinder<Texture2D>.Get( "button-active" );
        public static Texture2D Circle = ContentFinder<Texture2D>.Get( "circle" );
        public static Texture2D EW = ContentFinder<Texture2D>.Get( "ew" );
        public static Texture2D NS = ContentFinder<Texture2D>.Get( "ns" );
        public static Texture2D End = ContentFinder<Texture2D>.Get( "end" );
        public static Texture2D MoreIcon = ContentFinder<Texture2D>.Get( "more" );

        public static bool Initialized;
        public const int MinTrunkSize = 2;

        public static void DrawLine( Pair<Node, Node> connection )
        {
            DrawLine( connection.First.Left, connection.Second.Right,
                      connection.Second.Research.IsFinished ? connection.Second.Tree.MediumColor : connection.Second.Tree.GreyedColor );
        }

        public static void DrawLine( Vector2 a, Vector2 b, Color color )
        {
            GUI.color = color;

            Vector2 left, right;
            // make sure line goes left -> right
            if ( a.x < b.x )
            {
                left = a;
                right = b;
            }
            else
            {
                left = b;
                right = a;
            }

            // if left and right are on the same level, just draw a straight line.
            if( Math.Abs( left.y - right.y ) < 0.1f )
            {
                Rect line = new Rect( left.x, left.y - 2f, right.x - left.x, 4f );
                GUI.DrawTexture( line, EW );
            }

            // draw three line pieces and two curves.
            else
            {
                // determine top and bottom y positions
                float top = Math.Min(left.y, right.y) + Settings.Margin.x / 4f;
                float bottom = Math.Max(left.y, right.y) - Settings.Margin.x / 4f;

                // left to curve
                Rect leftToCurve = new Rect( left.x, left.y - 2f, Settings.Margin.x / 4f, 4f );
                GUI.DrawTexture( leftToCurve, EW );
                
                // curve to curve
                Rect curveToCurve = new Rect( left.x + Settings.Margin.x / 2f - 2f, top, 4f, bottom - top );
                GUI.DrawTexture( curveToCurve, NS );

                // curve to right
                Rect curveToRight = new Rect( left.x + Settings.Margin.x / 4f * 3, right.y - 2f, right.x - left.x - Settings.Margin.x / 4f * 3, 4f );
                GUI.DrawTexture( curveToRight, EW );
                
                // curve positions
                Rect curveLeft = new Rect( left.x + Settings.Margin.x / 4f, left.y - Settings.Margin.x / 4f, Settings.Margin.x / 2f, Settings.Margin.x / 2f);
                Rect curveRight = new Rect( left.x + Settings.Margin.x / 4f, right.y - Settings.Margin.x / 4f, Settings.Margin.x / 2f, Settings.Margin.x / 2f);

                // going down
                if( left.y < right.y )
                {
                    GUI.DrawTextureWithTexCoords( curveLeft, Circle, new Rect( 0.5f, 0.5f, 0.5f, 0.5f ) ); // bottom right quadrant
                    GUI.DrawTextureWithTexCoords( curveRight, Circle, new Rect( 0f, 0f, 0.5f, 0.5f ) ); // top left quadrant
                }
                // going up
                else
                {
                    GUI.DrawTextureWithTexCoords( curveLeft, Circle, new Rect( 0.5f, 0f, 0.5f, 0.5f ) ); // top right quadrant
                    GUI.DrawTextureWithTexCoords( curveRight, Circle, new Rect( 0f, 0.5f, 0.5f, 0.5f ) ); // bottom left quadrant
                }
            }

            // draw the end arrow
            Rect end = new Rect( right.x - 12f, right.y - 8f, 16f, 16f );
            GUI.DrawTexture( end, End );

            // reset color
            GUI.color = Color.white;
        }

        public static void Initialize()
        {
            // populate all nodes
            Forest = new List<Node>( DefDatabase<ResearchProjectDef>.AllDefsListForReading
                                        // exclude hidden projects (prereq of itself is a common trick to hide research).
                                        .Where( def => !def.prerequisites.Contains( def ) )
                                        .Select( def => new Node( def ) ) );

            // remove redundant prerequisites.
            foreach( Node node in Forest )
            {
                if ( !node.Research.prerequisites.NullOrEmpty() )
                {
                    var ancestors = node.Research.prerequisites.SelectMany( r => r.GetPrerequisitesRecursive() );
#if DEBUG
                    if( !node.Research.prerequisites.Intersect( ancestors ).ToList().NullOrEmpty() )
                    {
                        Log.Message( "ResearchTree :: redundant prerequisites for " + node.Research.LabelCap + " removed: " +
                            string.Join( ", ", node.Research.prerequisites.Intersect( ancestors ).Select( r => r.LabelCap ).ToArray() ) );
                    }
#endif
                    node.Research.prerequisites = node.Research.prerequisites.Except( ancestors ).ToList();
                }
            }

            // create links between nodes
            foreach ( Node node in Forest )
            {
                node.CreateLinks();
            }

            // calculate Depth of each node
            foreach ( Node node in Forest )
            {
                node.SetDepth();
            }
            
            // get the main 'Trees', looping over all defs, find strings of Research named similarly.
            // We're aiming for finding things like Construction I/II/III/IV/V here.
            Dictionary<string, List<Node>> trunks = new Dictionary<string, List<Node>>();
            List<Node> orphans = new List<Node>(); // temp
            foreach ( Node node in Forest )
            {
                // try to remove the amount of random hits by requiring Trees to be directly linked.
                if( node.Parents.Any( parent => parent.Genus == node.Genus ) ||
                     node.Children.Any( child => child.Genus == node.Genus ) )
                {
                    if ( !trunks.ContainsKey( node.Genus ) )
                    {
                        trunks.Add( node.Genus, new List<Node>() );
                    }
                    trunks[node.Genus].Add( node );
                }
                else
                {
                    orphans.Add( node );
                }
            }

            // Assign the working dictionary to Tree objects, culling stumps.
            Trees = trunks.Where( trunk => trunk.Value.Count >= MinTrunkSize )
                            .Select( trunk => new Tree( trunk.Key, trunk.Value ) )
                            .ToList();

            // add too small Trees back into orphan list
            orphans.AddRange( trunks.Where( trunk => trunk.Value.Count < MinTrunkSize ).SelectMany( trunk => trunk.Value ) );

            // The order in which Trees should appear; ideally we want Trees with lots of cross-references to appear together.
            OrderTrunks();
            
            // Attach orphan nodes to the nearest Trunk, or the orphanage trunk
            Orphans = new Tree( "orphans", new List<Node>() ) { Color = Color.grey };
            foreach ( Node orphan in orphans )
            {
                Tree closest = orphan.ClosestTree() ?? Orphans;
                closest.AddLeaf( orphan );
            }

            // Assign colors to trunks
            int n = Trees.Count;
            for( int i = 1; i <= Trees.Count; i++ )
            {
                Trees[i - 1].Color = ColorHelper.HSVtoRGB( (float)i / n, 1, 1 );
            }
            
            // update nodes with position info
            FixPositions();

            // Done!
            Initialized = true;
        }

        private static void OrderTrunks()
        {
            // if two or less Trees, optimization is pointless
            if ( Trees.Count < 3 ) return;

            // This is a form of the travelling salesman problem, but let's simplify immensely by taking a nearest-neighbour approach.
            List<Tree> trees = Trees.OrderBy( tree => - tree.Leaves.Count ).ToList();
            Trees.Clear();

            // initialize list of Trees with the largest
            Tree first = trees.First();
            Trees.Add( first );
            trees.Remove( first );

            // Set up a weighting system to keep 2nd highest affinity closer to 1st highest affinity
            Dictionary<Tree, float> weights =
                new Dictionary<Tree, float>( trees.ToDictionary( tree => tree, tree => first.AffinityWith( tree ) ) );

            // add other Trees
            while ( trees.Count > 0 )
            {
                // get tree with highest accumulated weight
                Tree next = weights.Where( pair => trees.Contains( pair.Key ) ).MaxBy( pair => pair.Value ).Key;
                Trees.Add( next );
                trees.Remove( next );

                // add weights for next set
                foreach ( Tree tree in trees )
                {
                    weights[tree] += next.AffinityWith( tree );
                }
            }
        }

        public static void FixPositions()
        {
            int curY = 0;

            #region Tree Node positions
            foreach( Tree tree in Trees )
            {
                tree.StartY = curY;
                
                foreach( Node node in tree.Trunk )
                {
                    int bestPos = curY;
                    // trunks can have (but shouldn't have) more than one node at each depth.
                    while( tree.Trunk.Any( otherNode => otherNode.Pos.z == bestPos && otherNode.Depth == node.Depth ) )
                    {
                        bestPos++;
                    }
                    node.Pos = new IntVec2( node.Depth, bestPos );

                    // extend tree width if necessary
                    tree.Width = Math.Max( tree.Width, bestPos - curY + 1 );
                }

                // position child nodes as close to their parents as possible
                for ( int x = tree.MinDepth; x <= tree.MaxDepth; x++ )
                {
                    // put nodes that are children of the trunk first.
                    List<Node> nodes = tree.NodesAtDepth( x ).OrderBy( node => node.Parents.Any( parent => node.Tree.Trunk.Contains( parent )) ? 0 : 1 ).ToList();
                    List<Node> allNodesAtCurrentDepth = tree.NodesAtDepth( x, true );

                    foreach ( Node node in nodes )
                    {
                        // try find the closest matching position, default to right below trunk
                        int bestPos = curY + 1;

                        // if we have any parent research in this trunk, try to get positioned next to it.
                        if (node.Parents.Any( parent => parent.Tree == node.Tree ))
                            bestPos = node.Parents.Where( parent => parent.Tree == node.Tree ).Select( parent => parent.Pos.z ).Min();

                        // bump down if taken by any node in tree
                        while ( allNodesAtCurrentDepth.Any( n => n.Pos.z == bestPos ) || bestPos == curY )
                            bestPos++;

                        // extend tree width if necessary
                        tree.Width = Math.Max( tree.Width, bestPos - tree.StartY + 1 );

                        // set position
                        node.Pos = new IntVec2( node.Depth, bestPos );
                    }
                }

                // sort all nodes by their depths, then by their z position.
                tree.Leaves = tree.Leaves.OrderBy( node => node.Depth ).ThenBy( node => node.Pos.z ).ToList();

                // do a reverse pass to position parent nodes next to their children
                for ( int x = tree.MaxDepth; x >= tree.MinDepth; x-- )
                {
                    List<Node> nodes = tree.NodesAtDepth( x );
                    List<Node> allNodesAtCurrentDepth = tree.NodesAtDepth( x, true );
                    
                    foreach ( Node node in nodes )
                    {
                        // if this node has children;
                        if ( node.Children.Count > 0 )
                        {
                            // ideal position would be right next to top child, but we won't allow it to go out of tree bounds
                            Node topChild = node.Children.OrderBy( child => child.Pos.z ).First();
                            int bestPos = Math.Max( topChild.Pos.z, node.Tree.StartY + 1 );

                            // keep checking until we have a decent position
                            // if that is indeed the current position, great, move to next
                            if ( bestPos == node.Pos.z )
                                continue;
                                                       
                            // otherwise, check if position is taken by any node, or if the new position falls outside of tree bounds (exclude this node itself from matches)
                            while ( allNodesAtCurrentDepth.Any( n => n.Pos.z == bestPos && n != node ) )
                            {
                                //Log.Message( "Pos: " + bestPos );
                                //// does the node at that position have the same child, and is it part of the same tree?
                                //Node otherNode = nodes.First(n => n.Pos.z == bestPos);
                                //if ( !otherNode.Children.Contains( topChild ) && otherNode.Tree == node.Tree )
                                //{
                                //    // if not, switch them around
                                //    bestPos = otherNode.Pos.z;
                                //    otherNode.Pos.z = node.Pos.z;
                                //}
                                //// or just bump it down otherwise
                                //else
                                //{
                                    bestPos++;
                                //}
                            }

                            // we should now have a decent position
                            // extend tree width if necessary
                            tree.Width = Math.Max( tree.Width, bestPos - tree.StartY + 1 );

                            // set position
                            node.Pos = new IntVec2( node.Depth, bestPos );
                        }
                    }
                }
                
                curY += tree.Width;
            }
            #endregion Tree Node Positions

            #region Orphan grid
            // try and get root nodes first
            IEnumerable<Node> roots = Orphans.Leaves.Where( node => node.Children.Any() && !node.Parents.Any() ).OrderBy( node => node.Depth );
            int rootYOffset = 0;
            foreach ( Node root in roots )
            {
                // set position
                root.Pos = new IntVec2( root.Depth, curY + rootYOffset );

                // recursively go through all children
                // width at depths
                Dictionary<int, int> widthAtDepth = new Dictionary<int, int>();
                Stack<Node> children = new Stack<Node>( root.Children );
                while ( children.Any() )
                {
                    // get node
                    Node child = children.Pop();

                    // continue if already positioned
                    if ( child.Pos != IntVec2.Zero )
                        continue;

                    // get width at current depth
                    int width;
                    if ( !widthAtDepth.ContainsKey( child.Depth ) )
                    {
                        widthAtDepth.Add( child.Depth, 0 );
                    }
                    width = widthAtDepth[child.Depth]++;

                    // set position
                    child.Pos = new IntVec2(child.Depth, curY + rootYOffset + width);
                    
                    // enqueue child's children
                    foreach ( Node grandchild in child.Children )
                    {
                        children.Push(grandchild);
                    }
                }

                // next root
                rootYOffset += widthAtDepth.Max().Value;
            }

            // update orphan width for mini tree(s)
            ResearchTree.Orphans.Width = rootYOffset;
            curY += rootYOffset;

            // create orphan grid
            int nodesPerRow = (int)( Screen.width / ( Settings.Button.x + Settings.Margin.x ) );
            List<Node> orphans = Orphans.Leaves.Where( node => !node.Parents.Any() && !node.Children.Any() ).OrderBy( node => node.Research.LabelCap ).ToList();

            // set positions
            for ( int i = 0; i < orphans.Count; i++ )
            {
                orphans[i].Pos = new IntVec2( i % nodesPerRow, i / nodesPerRow + curY );
            }

            // update width + depth
            Orphans.Width += Mathf.CeilToInt( (float)orphans.Count / (float)nodesPerRow );
            Orphans.MaxDepth = Math.Max( Orphans.MaxDepth, nodesPerRow - 1 ); // zero-based

            #endregion
        }
    }
}