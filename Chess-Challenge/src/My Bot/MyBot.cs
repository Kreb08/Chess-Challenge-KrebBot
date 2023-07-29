﻿//#define LOG
using ChessChallenge.API;
using System;
using System.Drawing;
using System.Linq;

//Tier 1: -128 Elo  +/- 30 (430 Games)
public class MyBot : IChessBot
{
    // Piece values: null, pawn, knight, bishop, rook, queen, king
    private int[] pieceValues;
    private int[] pieceValuesE = { 0, 100, 330, 300, 500, 900, 0 };
    private int[] pieceValuesM = { 0, 150, 300, 330, 500, 900, 0 };
    private int[] pieceValuesL = { 0, 400, 200, 330, 500, 900, 10000 };

    private const byte INVALID = 0, EXACT = 1, LOWERBOUND = 2, UPPERBOUND = 3;
    private const ulong k_TpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
    private Transposition[] transposTable; // ref  m_TPTable[zHash & k_TpMask]

    int max_depth = 8;
    int max_time = 0;

#if LOG
    int cacheHits = 0;
    int nodes = 0;
    #endif

    public MyBot()
    {
        transposTable = new Transposition[k_TpMask + 1];
    }

    public Move Think(Board board, Timer timer)
    {
        
        if (board.PlyCount < 20 && BitboardHelper.GetNumberOfSetBits(board.AllPiecesBitboard) > 20)
        {
            // EARLY
            pieceValues = pieceValuesE;
        }
        else if (BitboardHelper.GetNumberOfSetBits(board.AllPiecesBitboard) > 10)
        {
            // MIDGAME
            pieceValues = pieceValuesM;
        } else
        {
            // LATE
            pieceValues = pieceValuesL;
        }
        max_time = timer.MillisecondsRemaining / 30;
        Transposition bestMove = transposTable[board.ZobristKey & k_TpMask];
        for (sbyte i = 1; i <= max_depth; i++)
        {
#if LOG
            evals = 0;
            nodes = 0;
#endif
            CalcRecursive(board, timer, i, board.IsWhiteToMove ? 1 : -1);
            bestMove = transposTable[board.ZobristKey & k_TpMask];
#if LOG
            Console.WriteLine("Depth: {0,2} | Nodes: {1,10} | Evals: {2,10} | Time: {3,5} ms | Best {4} | Eval: {5}", i, nodes, evals, timer.MillisecondsElapsedThisTurn, bestMove.move, bestMove.evaluation);
#endif
            if ((max_time - timer.MillisecondsElapsedThisTurn) < timer.MillisecondsElapsedThisTurn * Math.Pow(i+1, 3))
            {
                break;
            }
        }
        return bestMove.move;
    }

    int CalcRecursive(Board board, Timer timer, sbyte currentDepth, int side)
    {
#if LOG
        nodes++;
#endif

        ref Transposition transposition = ref transposTable[board.ZobristKey & k_TpMask];

        if (transposition.zobristHash == board.ZobristKey && transposition.flag != INVALID && transposition.depth >= currentDepth) return transposition.evaluation;


        if (board.IsInCheckmate()) return int.MinValue + board.PlyCount;
        if (board.IsDraw()) return -10;
        if (currentDepth <= 0) return EvalMaterial(board, side);

        Move[] moves = board.GetLegalMoves();
        Move bestMove = moves[0];
        int bestScore = int.MinValue;
        foreach (Move move in moves.OrderByDescending(m => pieceValues[(int)m.MovePieceType]))
        {
            board.MakeMove(move);
            int score = -CalcRecursive(board, timer, (sbyte) (currentDepth - 1), -side);
            board.UndoMove(move);
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
            if (timer.MillisecondsElapsedThisTurn > max_time) break;
        }

        transposition.zobristHash = board.ZobristKey;
        transposition.move = bestMove;
        transposition.evaluation = bestScore;
        transposition.flag = EXACT;
        transposition.depth = currentDepth;
        return bestScore;
    }

    int EvalMaterial(Board board, int side)
    {
#if LOG
        evals++;
#endif
        int materialValue = board.GetAllPieceLists().Chunk(6).Select(chunk => chunk.Take(5).Sum(list => list.Sum(p => pieceValues[(int)p.PieceType]))).Aggregate((white, black) => white - black) * side;
        return materialValue + Mobility(board, side);
    }

    int Mobility(Board board, int side)
    {
        int mobility = 0;
        // ignore pawns and king
        for (int pieceType = 2; pieceType < 6; pieceType++)
        {
            // get piecelist for relevant pieces, getAttackMoves, exclude own piece positions, sum of tiles
            mobility += board.GetPieceList((PieceType) pieceType, side == 1).Sum(p => BitboardHelper.GetNumberOfSetBits(~(side == 1 ? board.WhitePiecesBitboard : board.BlackPiecesBitboard) & BitboardHelper.GetPieceAttacks(p.PieceType, p.Square, board, side == 1)));
        }
        return mobility;
    }

    /*
    int GetDistance(Square square1, Square square2)
    {
        return Math.Abs(square1.File - square2.File) + Math.Abs(square1.Rank - square2.Rank);
    }
    */

    //14 bytes per entry, likely will align to 16 bytes due to padding (if it aligns to 32, recalculate max TP table size)
    public struct Transposition
    {
        public ulong zobristHash;
        public Move move;
        public int evaluation;
        public sbyte depth;
        public byte flag;
    };
}