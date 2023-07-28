//#define LOGGING
using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

public class MyBot : IChessBot
{
    // Piece values: null, pawn, knight, bishop, rook, queen, king
    private int[] pieceValues = { 0, 100, 300, 330, 500, 900, 10000 };

    private const byte INVALID = 0, EXACT = 1, LOWERBOUND = 2, UPPERBOUND = 3;
    private const ulong k_TpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
    private Transposition[] transposTable; // ref  m_TPTable[zHash & k_TpMask]

    readonly int maxPieceSum;

    int m_depth = 4;
    int maxMillisPerTurn;

    public MyBot()
    {
        transposTable = new Transposition[k_TpMask + 1];
        maxPieceSum = pieceValues[1] * 8 + pieceValues[2] * 2 + pieceValues[3] * 2 + pieceValues[4] * 2 + pieceValues[5] + pieceValues[6];
    }

    public Move Think(Board board, Timer timer)
    {
        int pieces = BitboardHelper.GetNumberOfSetBits(board.AllPiecesBitboard);
        if (pieces < 15) m_depth = 5;
        else if (pieces <= 10) m_depth = 6;
        else if (pieces <= 8) m_depth = 7;
        else if (pieces <= 6) m_depth = 8;

        maxMillisPerTurn = timer.MillisecondsRemaining / 10;

        List<Move> bestMoves = new List<Move>();
        int bestScore = int.MinValue;
        foreach (Move move in board.GetLegalMoves().OrderByDescending(m => pieceValues[(int)m.MovePieceType])) {
            board.MakeMove(move);
            int score = CalcRecursive(board, timer, m_depth, 1);
            board.UndoMove(move);

            if (score > bestScore)
            {
                bestMoves.Clear();
                bestScore = score;
            }
            if (score >= bestScore) bestMoves.Add(move);
        }
        return SelectBestMove(board, bestMoves);
    }

    int EvalMaterial(Board board)
    {
        return board.GetAllPieceLists().Chunk(6).Select(chunk => chunk.Sum(list => list.Sum(p => pieceValues[(int)p.PieceType]))).Aggregate((white, black) => white - black);
    }

    int CalcRecursive(Board board, Timer timer, int maxDepth, int currentDepth)
    {
        if (board.IsInCheckmate()) return currentDepth % 2 == 0 ? board.PlyCount - maxPieceSum : maxPieceSum - board.PlyCount;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter == 50 || board.IsDraw()) return 0;

        Transposition transposition = transposTable[board.ZobristKey & k_TpMask];

        if (transposition.zobristHash == board.ZobristKey /* && transposition.flag != INVALID && transposition.depth >= 0 */)
        {
            return transposition.evaluation;
        }
        if (currentDepth >= maxDepth)
        {
            int boardScore = (currentDepth % 2 == 0) ^ board.IsWhiteToMove ? -EvalMaterial(board) : EvalMaterial(board);
            transposition.evaluation = boardScore;
            return boardScore;
        }

        int bestScore = int.MinValue;
        foreach (Move move in board.GetLegalMoves().OrderByDescending(m => pieceValues[(int)m.MovePieceType]))
        {
            board.MakeMove(move);
            int score = CalcRecursive(board, timer, maxDepth, currentDepth + 1);
            board.UndoMove(move);
            if (score > bestScore) bestScore = score;
            if (timer.MillisecondsElapsedThisTurn > maxMillisPerTurn) return bestScore;
        }
        return bestScore;
    }

    Move SelectBestMove(Board board, List<Move> moves)
    {
        Move bestMove = moves[new Random().Next(moves.Count)];
        int bestValue = int.MinValue;

        foreach (Move move in moves)
        {
            Square kingSquare = board.GetKingSquare(!board.IsWhiteToMove);
            int promotionValue = pieceValues[(int)move.PromotionPieceType];
            int captureValue = pieceValues[(int)move.CapturePieceType];
            int lostPieceValue = HighestLostPiece(board, move);
            int progress = move.MovePieceType == PieceType.Pawn ? GetDistance(move.StartSquare, move.TargetSquare) : GetDistance(kingSquare, move.StartSquare) - GetDistance(kingSquare, move.TargetSquare);

            int score = captureValue - lostPieceValue + promotionValue + progress;
            if (score > bestValue)
            {
                bestMove = move;
                bestValue = score;
            }
        }

        return bestMove;
    }

    int HighestLostPiece(Board board, Move move) {
        board.MakeMove(move);
        int maxValue = board.GetAllPieceLists().Chunk(6).ElementAt(board.IsWhiteToMove ? 1 : 0).SelectMany(list => list)
            .Where(p => board.GetLegalMoves(true).Select(m => m.TargetSquare).Contains(p.Square)).Where(p => !board.SquareIsAttackedByOpponent(p.Square)).Select(p => pieceValues[(int)p.PieceType]).DefaultIfEmpty(0).Max();
        board.UndoMove(move);
        return maxValue;
    }

    int GetDistance(Square square1, Square square2)
    {
        return Math.Abs(square1.File - square2.File) + Math.Abs(square1.Rank - square2.Rank);
    }

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