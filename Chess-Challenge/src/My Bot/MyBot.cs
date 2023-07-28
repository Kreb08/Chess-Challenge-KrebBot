#define LOG
using ChessChallenge.API;
using System;
using System.Linq;

public class MyBot : IChessBot
{
    // Piece values: null, pawn, knight, bishop, rook, queen, king
    private int[] pieceValues = { 0, 100, 300, 330, 500, 900, 10000 };

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
        max_time = timer.MillisecondsRemaining / 20;
        Transposition bestMove = transposTable[board.ZobristKey & k_TpMask];
        for (sbyte i = 1; i <= max_depth; i++)
        {
#if LOG
            cacheHits = 0;
            nodes = 0;
#endif
            CalcRecursive(board, i, board.IsWhiteToMove ? 1 : -1);
            bestMove = transposTable[board.ZobristKey & k_TpMask];
#if LOG
            Console.WriteLine("Depth: {0,2} | Nodes: {1,10} | Time: {2,5} ms | Best {3} | Eval: {4}", i, nodes, timer.MillisecondsElapsedThisTurn, bestMove.move, bestMove.evaluation);
#endif
            if ((max_time - timer.MillisecondsElapsedThisTurn) < timer.MillisecondsElapsedThisTurn * Math.Pow(i+1, 3))
            {
                break;
            }
        }
        return bestMove.move;
    }

    int CalcRecursive(Board board, sbyte currentDepth, int side)
    {
#if LOG
        nodes++;
#endif

        ref Transposition transposition = ref transposTable[board.ZobristKey & k_TpMask];

        if (transposition.zobristHash == board.ZobristKey && transposition.flag != INVALID && transposition.depth >= currentDepth)
        {
#if LOG
            cacheHits++;
#endif
            return transposition.evaluation;
        }

        if (board.IsInCheckmate()) return int.MinValue + board.PlyCount;
        if (board.IsDraw()) return -10;
        if (currentDepth <= 0) return EvalMaterial(board, side) * side;

        Move[] moves = board.GetLegalMoves();
        Move bestMove = moves[0];
        int bestScore = int.MinValue;
        foreach (Move move in moves.OrderByDescending(m => pieceValues[(int)m.MovePieceType]))
        {
            board.MakeMove(move);
            int score = -CalcRecursive(board, (sbyte) (currentDepth - 1), -side);
            board.UndoMove(move);
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
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
        //Square kingSquare = board.GetKingSquare(side == 1);
        int repetition = board.IsRepeatedPosition() ? 100 : 0;
        int materialValue = board.GetAllPieceLists().Chunk(6).Select(chunk => chunk.Sum(list => list.Sum(p => pieceValues[(int)p.PieceType]))).Aggregate((white, black) => white - black);
        return materialValue - repetition;
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